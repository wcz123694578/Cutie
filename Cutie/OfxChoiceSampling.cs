using ScriptPortal.Vegas;
using System;
using System.Reflection;
using System.Reflection.Emit;

namespace Cutie
{
    internal static class OfxChoiceSampling
    {
        private const int ENotImpl = unchecked((int)0x80004001);
        private delegate void NativeSample(object target, int parameterIndex, long nanos, out int choiceIndex);
        private static readonly Lazy<NativeSample> Sample = new Lazy<NativeSample>(CreateSampler);
        private static readonly FieldInfo ComField = typeof(OFXParameter).GetField("myCOM",
            BindingFlags.Instance | BindingFlags.NonPublic);

        internal static OFXChoice GetValueAtTime(OFXChoiceParameter parameter, Timecode time)
        {
            if (parameter == null) throw new ArgumentNullException(nameof(parameter));
            if (time == null) throw new ArgumentNullException(nameof(time));
            try { return parameter.GetValueAtTime(time); }
            catch (NotImplementedException error) when (error.HResult == ENotImpl && IsAffectedVersion())
            {
                // These hosts calculate the index, but return E_NOTIMPL afterwards.
                // An empty animated curve can return the same error with only a zeroed output.
                var animated = parameter.IsAnimated;
                var keyCount = parameter.Keyframes.Count;
                if (animated && keyCount == 0) throw;
                var choices = parameter.Choices;
                if (choices == null || choices.Length == 0 || ComField == null) throw;
                var target = ComField.GetValue(parameter);
                if (target == null) throw;

                var index = -1;
                try { Sample.Value(target, parameter.Index, time.Nanos, out index); }
                catch (NotImplementedException nativeError) when (nativeError.HResult == ENotImpl)
                {
                    // A caller-owned, blittable out int survives the COM exception.
                    // Do not substitute values for other HRESULTs or exceptions.
                }

                if (parameter.IsAnimated != animated || parameter.Keyframes.Count != keyCount ||
                    index < 0 || index >= choices.Length ||
                    (keyCount == 0 && parameter.Value.Index != index))
                    throw new InvalidOperationException("VEGAS Choice sampling did not produce a verifiable index.", error);
                return choices[index];
            }
        }

        private static bool IsAffectedVersion()
        {
            var version = typeof(OFXParameter).Assembly.GetName().Version;
            return version == new Version(18, 0, 0, 527) || version == new Version(2026, 0, 3, 189);
        }

        private static NativeSample CreateSampler()
        {
            var contract = ComField?.FieldType;
            var method = contract?.GetMethod("GetChoiceAt", new[]
            {
                typeof(int), typeof(long), typeof(int).MakeByRefType()
            });
            if (contract == null || !contract.IsInterface ||
                contract.GUID != new Guid("8E8B9519-FE99-4AD5-8E44-5A3E37DA759B") ||
                method == null || method.ReturnType != typeof(void) || !method.GetParameters()[2].IsOut)
                throw new NotSupportedException("The loaded VEGAS Choice COM interface is not compatible.");

            // Bind to the loaded interface rather than assuming native addresses or vtable slots.
            var bridge = new DynamicMethod("SampleVegasChoice", typeof(void), new[]
            {
                typeof(object), typeof(int), typeof(long), typeof(int).MakeByRefType()
            }, typeof(OfxChoiceSampling).Module, true);
            var il = bridge.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, contract);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Callvirt, method);
            il.Emit(OpCodes.Ret);
            return (NativeSample)bridge.CreateDelegate(typeof(NativeSample));
        }
    }
}
