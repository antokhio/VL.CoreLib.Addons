using System.Reactive.Disposables;
using VL.Core;
using VL.Core.Import;
using VL.Core.PublicAPI;
using VL.Lang;

namespace VL.CoreLib.Addons.Primitives
{
    /// <summary>
    /// Executes the patch inside only if the input is assigned (not null) and passes the value into it.
    /// Accumulators pass their values through. In frames the patch doesn't run, each accumulator output
    /// returns the value of its input.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="IsAssignedRegion{T}"/>, <typeparamref name="T"/> isn't constrained to reference
    /// types. If it can't be null, the node shows a warning and the patch inside always runs.
    /// </remarks>
    [ProcessNode(FragmentSelection = FragmentSelection.Explicit)]
    [Region(SupportedBorderControlPoints = ControlPointType.Accumulator)]
    public sealed class IsAssignedRegion2<T> : IRegion<IIsAssignedInlay<T>>, IDisposable
    {
        internal static readonly bool CanBeNull = default(T) is null;

        internal static readonly string NotNullableWarningMessage =
            $"The type {typeof(T).Name} can't be null, so the patch inside always runs.";

        private readonly Dictionary<InputDescription, object?> _inputs = new();
        private readonly Dictionary<OutputDescription, object?> _outputs = new();
        private readonly Dictionary<string, object?> _accumulatorInputs = new();
        private readonly Func<IDisposable> _addNotNullableWarning;

        private Func<IIsAssignedInlay<T>>? _factory;
        private IIsAssignedInlay<T>? _inlay;
        private IDisposable? _notNullableWarning;
        private bool _disposed;

        [Fragment]
        public IsAssignedRegion2(NodeContext nodeContext)
            : this(() => AddNotNullableWarning(nodeContext)) { }

        /// <summary>
        /// Test seam: allows to supply the way the warning is shown.
        /// </summary>
        internal IsAssignedRegion2(Func<IDisposable>? addNotNullableWarning = null)
        {
            _addNotNullableWarning = addNotNullableWarning ?? (() => Disposable.Empty);
        }

        private static IDisposable AddNotNullableWarning(NodeContext nodeContext)
        {
            // The runtime is resolved via AppHost.Current; ensure it is the app this node lives in.
            using var scope = nodeContext.AppHost.MakeCurrent();

            // Equivalent of NodeContext.AddPersistentMessage(..., includeAncestors: false), which
            // isn't available in VL.Core 2025.7.0. The top of the path is the node's local id.
            var runtime = IVLRuntime.Current;
            if (runtime is null)
                return Disposable.Empty;

            return runtime.AddPersistentMessage(
                new Message(
                    nodeContext.Path.Stack.Peek(),
                    MessageSeverity.Warning,
                    NotNullableWarningMessage
                )
            );
        }

        void IRegion<IIsAssignedInlay<T>>.SetPatchInlayFactory(
            Func<IIsAssignedInlay<T>> patchInlayFactory
        )
        {
            ArgumentNullException.ThrowIfNull(patchInlayFactory);

            // Called again with a new factory after each recompile. The existing inlay is kept on
            // purpose: VL forwards calls on an outdated inlay to its hot-swapped version, state included.
            _factory = patchInlayFactory;
        }

        /// <summary>
        /// Executes the patch inside if <paramref name="input"/> is assigned.
        /// </summary>
        /// <param name="input">The value to check.</param>
        /// <param name="isAssigned">Whether the input was assigned and the patch got executed.</param>
        [Fragment]
        public void Update(T? input, out bool isAssigned)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!CanBeNull)
                EnsureNotNullableWarning();

            // Values written by the patch in a previous frame must not leak into a frame it didn't run in.
            _outputs.Clear();

            isAssigned = input is not null;
            if (input is null)
                return;

            var factory =
                _factory
                ?? throw new InvalidOperationException(
                    "Patch inlay factory has not been assigned."
                );

            _inlay ??= factory();
            _inlay.Update(input);
        }

        private void EnsureNotNullableWarning()
        {
            if (_notNullableWarning is not null)
                return;

            // Empty means no runtime was available; don't keep it so the next call retries.
            var warning = _addNotNullableWarning();
            if (!ReferenceEquals(warning, Disposable.Empty))
                _notNullableWarning = warning;
        }

        // Called for accumulators and for links crossing into the region.
        void IRegion<IIsAssignedInlay<T>>.AcknowledgeInput(
            in InputDescription description,
            object? outerValue
        )
        {
            _inputs[description] = outerValue;

            if (description.AccumulatorId is { } accumulatorId)
                _accumulatorInputs[accumulatorId] = outerValue;
        }

        void IRegion<IIsAssignedInlay<T>>.RetrieveInput(
            in InputDescription description,
            IIsAssignedInlay<T> patchInlay,
            out object? innerValue
        )
        {
            _inputs.TryGetValue(description, out innerValue);
        }

        void IRegion<IIsAssignedInlay<T>>.AcknowledgeOutput(
            in OutputDescription description,
            IIsAssignedInlay<T> patchInlay,
            object? innerValue
        )
        {
            _outputs[description] = innerValue;
        }

        // VL turns null (e.g. an accumulator without a connected input) into the default value of the output type.
        void IRegion<IIsAssignedInlay<T>>.RetrieveOutput(
            in OutputDescription description,
            out object? outerValue
        )
        {
            if (_outputs.TryGetValue(description, out outerValue))
                return;

            if (description.AccumulatorId is { } accumulatorId)
                _accumulatorInputs.TryGetValue(accumulatorId, out outerValue);
        }

        [Fragment]
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            var warning = _notNullableWarning;
            _notNullableWarning = null;
            warning?.Dispose();

            var inlay = _inlay;
            _inlay = null;
            (inlay as IDisposable)?.Dispose();

            _factory = null;
            _inputs.Clear();
            _outputs.Clear();
            _accumulatorInputs.Clear();
        }
    }
}
