using ScriptPortal.Vegas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace Cutie
{
    public class VegasContext
    {
        private static SynchronizationContext _synchronizationContext;

        public static Vegas Current { get; private set; }

        public static void Initialize(Vegas vegas, SynchronizationContext synchronizationContext)
        {
            Current = vegas;
            _synchronizationContext = synchronizationContext
                ?? throw new ArgumentNullException(nameof(synchronizationContext));
        }

        public static Task<T> InvokeAsync<T>(Func<T> action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            var context = _synchronizationContext
                ?? throw new InvalidOperationException("VEGAS context has not been initialized.");
            var completion = new TaskCompletionSource<T>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            context.Post(_ =>
            {
                try
                {
                    completion.SetResult(action());
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            }, null);

            return completion.Task;
        }
    }
}
