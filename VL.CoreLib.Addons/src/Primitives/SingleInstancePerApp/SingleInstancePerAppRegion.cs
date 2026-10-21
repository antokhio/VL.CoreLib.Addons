using VL.Core;
using VL.Core.Import;
using VL.Core.PublicAPI;
using VL.Lib.Animation;
using VL.Lib.Basics.Resources;

namespace VL.CoreLib.Addons.Primitives
{
    /// <summary>
    /// A region whose patch exists only once per app. All instances of the same region node
    /// (e.g. inside multiple instances of the surrounding patch) share one patch inlay.
    /// Only the first <c>Update</c> call within a frame executes the patch; all other instances
    /// get the cached value of that frame.
    /// </summary>
    [ProcessNode(
        Name = "SingleInstancePerApp (Statefull)",
        FragmentSelection = FragmentSelection.Explicit
    )]
    [Region(SupportedBorderControlPoints = ControlPointType.None)]
    public sealed class SingleInstancePerAppRegion<T>
        : IRegion<SingleInstancePerAppRegion<T>.ISingleInstanceInlay<T>>,
            IDisposable
    {
        public interface ISingleInstanceInlay<T>
        {
            T Produce();
        }

        private readonly NodeContext _context;

        private Func<ISingleInstanceInlay<T>>? _factory;
        private IResourceHandle<SharedInstance>? _handle;
        private bool _disposed;

        [Fragment]
        public SingleInstancePerAppRegion(NodeContext nodeContext)
        {
            _context = nodeContext;
        }

        void IRegion<ISingleInstanceInlay<T>>.SetPatchInlayFactory(
            Func<ISingleInstanceInlay<T>> patchInlayFactory
        )
        {
            ArgumentNullException.ThrowIfNull(patchInlayFactory);

            if (_factory is not null && _factory.Method != patchInlayFactory.Method)
                ReleaseHandle();

            _factory = patchInlayFactory;
        }

        [Fragment]
        public void Update(out T value)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SingleInstancePerAppRegion<T>));

            var factory =
                _factory
                ?? throw new InvalidOperationException(
                    "Patch inlay factory has not been assigned."
                );

            if (_handle is null)
            {
                // Path.Stack.Peek() identifies the region node itself and is the same for all instances
                // of the surrounding patch. The full path would be unique per instance.
                // The factory method distinguishes recompiled versions of the inner patch.
                var key = (_context.Path.Stack.Peek(), factory.Method);
                // Pool and frame clock are resolved via AppHost.Current; ensure it is the app this node lives in.
                using var scope = _context.AppHost.MakeCurrent();

                var frameClock = Clocks.FrameClock;

                _handle = ResourceProvider
                    .NewPooledPerApp(key, _ => new SharedInstance(frameClock))
                    .GetHandle();
            }

            value = _handle.Resource.UpdateOncePerFrame(factory);
        }

        // Border control points are not supported (see Region attribute), so there is no input
        // or output data to pass. The value is returned via Produce's typed return value.
        void IRegion<ISingleInstanceInlay<T>>.AcknowledgeInput(
            in InputDescription description,
            object? outerValue
        ) { }

        void IRegion<ISingleInstanceInlay<T>>.RetrieveInput(
            in InputDescription description,
            ISingleInstanceInlay<T> patchInlay,
            out object? innerValue
        )
        {
            innerValue = null;
        }

        void IRegion<ISingleInstanceInlay<T>>.AcknowledgeOutput(
            in OutputDescription description,
            ISingleInstanceInlay<T> patchInlay,
            object? innerValue
        ) { }

        void IRegion<ISingleInstanceInlay<T>>.RetrieveOutput(
            in OutputDescription description,
            out object? outerValue
        )
        {
            outerValue = null;
        }

        [Fragment]
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            ReleaseHandle();

            _factory = null;
        }

        private void ReleaseHandle()
        {
            var handle = _handle;
            _handle = null;
            handle?.Dispose();
        }

        /// <summary>
        /// The patch inlay shared by all region instances of one app with the same node id.
        /// </summary>
        private sealed class SharedInstance : IDisposable
        {
            private readonly object _lock = new();
            private readonly IDisposable _ticks;
            private ISingleInstanceInlay<T>? _inlay;
            private long _frame;
            private long _lastUpdatedFrame = -1;
            private T _value = default!;
            private bool _disposed;

            public SharedInstance(IFrameClock frameClock)
            {
                _ticks = frameClock.GetTicks().Subscribe(_ => Interlocked.Increment(ref _frame));
            }

            /// <summary>
            /// Executes the patch inlay on the first call within a frame. Subsequent calls within
            /// the same frame return the value of that first call.
            /// </summary>
            public T UpdateOncePerFrame(Func<ISingleInstanceInlay<T>> factory)
            {
                lock (_lock)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);

                    var frame = Interlocked.Read(ref _frame);
                    if (frame == _lastUpdatedFrame)
                        return _value;

                    _inlay ??= factory();
                    _value = _inlay.Produce();
                    _lastUpdatedFrame = frame;

                    return _value;
                }
            }

            public void Dispose()
            {
                _ticks.Dispose();
                lock (_lock)
                {
                    if (_disposed)
                        return;

                    _disposed = true;
                    (_inlay as IDisposable)?.Dispose();
                    _inlay = null;
                    _value = default!;
                }
            }
        }
    }
}
