using Iced.Intel;
using ScriptPortal.Vegas;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Cutie.Shared
{
    internal sealed class NativeTrackMove
    {
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int Reorder(IntPtr view, int insertionGap, int mode);

        private readonly Reorder _move;
        private readonly IntPtr _viewSlot;

        private NativeTrackMove(IntPtr function, IntPtr viewSlot)
        {
            _move = (Reorder)Marshal.GetDelegateForFunctionPointer(function, typeof(Reorder));
            _viewSlot = viewSlot;
        }

        internal void Move(int gap)
        {
            // Cache code addresses, not the context of a particular project.
            var view = Marshal.ReadIntPtr(_viewSlot);
            RequireReadable(view, 64);
            Marshal.ThrowExceptionForHR(_move(view, gap, 1));
        }

        internal static NativeTrackMove Resolve(Track track)
        {
            if (IntPtr.Size != 8) throw Unsupported();
            var assembly = typeof(Track).Assembly;
            var contract = assembly.GetType("ScriptPortal.Vegas.ITrackCOM");
            var getter = typeof(Track).GetMethod("GetCOM", BindingFlags.Instance | BindingFlags.NonPublic);
            var method = contract?.GetMethod("InsertTrackIntoGroup");
            if (getter == null || method == null || method.ReturnType != typeof(int) ||
                !method.GetParameters().Select(p => p.ParameterType).SequenceEqual(
                    new[] { typeof(uint), typeof(uint), typeof(int) }))
                throw Unsupported();

            var slot = Marshal.GetComSlotForMethodInfo(method);
            if (slot < 3 || slot > 4096) throw Unsupported();
            var com = getter.Invoke(track, null);
            var pointer = Marshal.GetComInterfaceForObject(com, contract);
            IntPtr entry;
            try
            {
                var table = Marshal.ReadIntPtr(pointer);
                RequireReadable(table, checked((slot + 1) * IntPtr.Size));
                entry = Marshal.ReadIntPtr(table, slot * IntPtr.Size);
            }
            finally
            {
                Marshal.Release(pointer);
            }

            using (var process = Process.GetCurrentProcess())
            {
                var image = new HostImage(process.MainModule);
                var wrapper = image.Rva(entry);
                var visited = new HashSet<uint>();
                for (var hop = 0; ; hop++)
                {
                    if (hop == 5 || !visited.Add(wrapper)) throw Unsupported();
                    var first = Decoder.Create(64, image.Read(wrapper, 15), wrapper).Decode();
                    if (first.FlowControl != FlowControl.UnconditionalBranch) break;
                    if (first.Op0Kind != OpKind.NearBranch64) throw Unsupported();
                    wrapper = checked((uint)first.NearBranchTarget);
                }

                var code = image.ReadFunction(wrapper);
                var call = FindCall(code, wrapper);
                image.FunctionEnd(call.Function);
                image.RequireSection(call.Function, 1, 0x20000000, 0x80000000);
                image.RequireSection(call.ViewSlot, 8, 0x80000000, 0x20000000);
                if (call.ViewSlot % 8 != 0) throw Unsupported();
                return new NativeTrackMove(image.Address(call.Function), image.Address(call.ViewSlot));
            }
        }

        internal struct CallSite
        {
            internal uint Function;
            internal uint ViewSlot;
        }

        internal static CallSite FindCall(byte[] bytes, uint address)
        {
            var decoder = Decoder.Create(64, bytes, address);
            var code = new List<Instruction>();
            var end = (ulong)address + (uint)bytes.Length;
            while (decoder.IP < end)
            {
                var instruction = decoder.Decode();
                if (instruction.IsInvalid || instruction.NextIP > end) throw Unsupported();
                code.Add(instruction);
            }

            var boundaries = new HashSet<ulong>(code.Select(i => i.IP));
            foreach (var instruction in code)
            {
                if ((instruction.FlowControl == FlowControl.ConditionalBranch ||
                    instruction.FlowControl == FlowControl.UnconditionalBranch) &&
                    (instruction.Op0Kind != OpKind.NearBranch64 || !boundaries.Contains(instruction.NearBranchTarget)))
                    throw Unsupported();
                if (instruction.FlowControl == FlowControl.IndirectBranch ||
                    instruction.FlowControl == FlowControl.Exception || instruction.FlowControl == FlowControl.Interrupt)
                    throw Unsupported();
            }
            if (code.Count == 0 || code[code.Count - 1].FlowControl != FlowControl.Return ||
                code.Count(i => i.FlowControl == FlowControl.Return) != 1)
                throw Unsupported();

            var matches = new List<CallSite>();
            for (var i = 3; i < code.Count; i++)
            {
                var view = code[i - 3];
                var mode = code[i - 2];
                var gap = code[i - 1];
                var call = code[i];
                if (view.Mnemonic != Mnemonic.Mov || view.Op0Register != Register.RCX || !view.IsIPRelativeMemoryOperand ||
                    !RegisterMove(mode, Register.R8D) || !RegisterMove(gap, Register.EDX) ||
                    call.FlowControl != FlowControl.Call || call.Op0Kind != OpKind.NearBranch64)
                    continue;

                var source = mode.Op1Register;
                var before = code.Take(i - 3).ToArray();
                if (!before.Any(p => p.Mnemonic == Mnemonic.Xor && p.Op0Register == source && p.Op1Register == source) ||
                    !before.Any(p => p.Mnemonic == Mnemonic.Setne && p.Op0Kind == OpKind.Register &&
                        p.Op0Register.GetFullRegister() == source.GetFullRegister()) ||
                    !before.Any(p => p.Mnemonic == Mnemonic.Add && p.Op0Register == source && Immediate(p, 3)) ||
                    !before.Any(p => p.Mnemonic == Mnemonic.Mov && p.Op0Register == source && Immediate(p, 2)))
                    continue;

                matches.Add(new CallSite
                {
                    Function = checked((uint)call.NearBranchTarget),
                    ViewSlot = checked((uint)view.IPRelativeMemoryAddress)
                });
            }
            if (matches.Count != 1) throw Unsupported();
            return matches[0];
        }

        private static bool RegisterMove(Instruction instruction, Register destination)
        {
            return instruction.Mnemonic == Mnemonic.Mov && instruction.Op0Kind == OpKind.Register &&
                instruction.Op0Register == destination && instruction.Op1Kind == OpKind.Register &&
                instruction.Op1Register.GetSize() == 4;
        }

        private static bool Immediate(Instruction instruction, uint value)
        {
            return (instruction.Op1Kind == OpKind.Immediate32 && instruction.Immediate32 == value) ||
                (instruction.Op1Kind == OpKind.Immediate8to32 && instruction.Immediate8to32 == value);
        }

        private sealed class HostImage
        {
            private readonly IntPtr _base;
            private readonly int _size;
            private readonly uint _functions;
            private readonly int _functionCount;
            private readonly uint _sections;
            private readonly int _sectionCount;

            internal HostImage(ProcessModule module)
            {
                _base = module.BaseAddress;
                _size = module.ModuleMemorySize;
                var pe = checked((uint)ReadInt32(0x3C));
                if ((ReadInt32(0) & 0xFFFF) != 0x5A4D || ReadInt32(pe) != 0x4550 ||
                    (ReadInt32(pe + 4) & 0xFFFF) != 0x8664 || (ReadInt32(pe + 24) & 0xFFFF) != 0x20B)
                    throw Unsupported();
                _functions = checked((uint)ReadInt32(pe + 24 + 136));
                var functionBytes = ReadInt32(pe + 24 + 140);
                if (functionBytes <= 0 || functionBytes % 12 != 0) throw Unsupported();
                Address(_functions, functionBytes);
                _functionCount = functionBytes / 12;
                _sectionCount = (ReadInt32(pe + 4) >> 16) & 0xFFFF;
                _sections = pe + 24 + (uint)(ReadInt32(pe + 20) & 0xFFFF);
                Address(_sections, checked(_sectionCount * 40));
            }

            internal uint Rva(IntPtr pointer)
            {
                var value = pointer.ToInt64() - _base.ToInt64();
                if (value < 0 || value >= _size) throw Unsupported();
                return (uint)value;
            }

            internal IntPtr Address(uint rva, int length = 1)
            {
                if (length < 1 || (ulong)rva + (uint)length > (ulong)_size) throw Unsupported();
                return IntPtr.Add(_base, checked((int)rva));
            }

            private int ReadInt32(uint rva) => Marshal.ReadInt32(Address(rva, 4));

            internal byte[] Read(uint rva, int length)
            {
                var pointer = Address(rva, length);
                RequireReadable(pointer, length);
                var bytes = new byte[length];
                Marshal.Copy(pointer, bytes, 0, length);
                return bytes;
            }

            internal byte[] ReadFunction(uint start)
            {
                var end = FunctionEnd(start);
                if (end - start > 8192) throw Unsupported();
                var length = checked((int)(end - start));
                RequireSection(start, length, 0x20000000, 0x80000000);
                return Read(start, length);
            }

            internal uint FunctionEnd(uint start)
            {
                var low = 0;
                var high = _functionCount - 1;
                while (low <= high)
                {
                    var middle = low + (high - low) / 2;
                    var item = _functions + (uint)(middle * 12);
                    var begin = unchecked((uint)ReadInt32(item));
                    if (begin < start) low = middle + 1;
                    else if (begin > start) high = middle - 1;
                    else
                    {
                        var end = unchecked((uint)ReadInt32(item + 4));
                        if (end <= start) throw Unsupported();
                        Address(start, checked((int)(end - start)));
                        return end;
                    }
                }
                throw Unsupported();
            }

            internal void RequireSection(uint rva, int length, uint required, uint forbidden)
            {
                Address(rva, length);
                for (var i = 0; i < _sectionCount; i++)
                {
                    var section = _sections + (uint)(i * 40);
                    var start = unchecked((uint)ReadInt32(section + 12));
                    var size = unchecked((uint)ReadInt32(section + 8));
                    var flags = unchecked((uint)ReadInt32(section + 36));
                    if (rva >= start && (ulong)rva + (uint)length <= (ulong)start + size &&
                        (flags & required) == required && (flags & forbidden) == 0)
                        return;
                }
                throw Unsupported();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryInfo
        {
            internal IntPtr BaseAddress, AllocationBase;
            internal uint AllocationProtect;
            internal UIntPtr RegionSize;
            internal uint State, Protect, Type;
        }

        [DllImport("kernel32.dll")]
        private static extern UIntPtr VirtualQuery(IntPtr address, out MemoryInfo info, UIntPtr length);

        private static void RequireReadable(IntPtr pointer, int length)
        {
            MemoryInfo memory;
            if (pointer == IntPtr.Zero || VirtualQuery(pointer, out memory,
                (UIntPtr)Marshal.SizeOf(typeof(MemoryInfo))) == UIntPtr.Zero ||
                memory.State != 0x1000 || (memory.Protect & 0x101) != 0 || (memory.Protect & 0xEE) == 0)
                throw Unsupported();
            var offset = (ulong)(pointer.ToInt64() - memory.BaseAddress.ToInt64());
            if (offset > memory.RegionSize.ToUInt64() || (ulong)length > memory.RegionSize.ToUInt64() - offset)
                throw Unsupported();
        }

        private static NotSupportedException Unsupported()
            => new NotSupportedException("Cannot resolve the native track move entry for this VEGAS version.");
    }
}
