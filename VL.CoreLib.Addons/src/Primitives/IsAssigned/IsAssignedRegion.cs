using VL.Core.Import;
using VL.Core.PublicAPI;

namespace VL.CoreLib.Addons.Primitives
{
    /// <summary>
    /// The patch inlay of the <see cref="IsAssignedRegion{T}"/>.
    /// </summary>
    /// <remarks>
    /// Declared outside of the region class on purpose, see <see cref="ISingleInstanceInlay{T}"/>.
    /// </remarks>
    public interface IIsAssignedInlay<T>
    {
        /// <summary>
        /// Executes the patch. The parameter shows up as a pin inside of the region.
        /// </summary>
        /// <param name="value">The assigned input value, guaranteed not to be null.</param>
        void Update(T value);
    }

    /// <summary>
    /// Executes the patch inside only if the input is assigned (not null) and passes the value into it.
    /// Accumulators pass their values through. In frames the patch doesn't run, each accumulator output
    /// returns the value of its input.
    /// </summary>
    /// <remarks>
    /// <typeparamref name="T"/> is constrained to reference types: value types can't be null, and without
    /// the constraint VL would infer them, making a connected NULL node throw at runtime.
    /// </remarks>
    [ProcessNode(FragmentSelection = FragmentSelection.Explicit)]
    [Region(SupportedBorderControlPoints = ControlPointType.Accumulator)]
    public sealed class IsAssignedRegion<T> : IRegion<IIsAssignedInlay<T>>, IDisposable
        where T : class
    {
        private readonly Dictionary<InputDescription, object?> _inputs = new();
        private readonly Dictionary<OutputDescription, object?> _outputs = new();
        private readonly Dictionary<string, object?> _accumulatorInputs = new();

        private Func<IIsAssignedInlay<T>>? _factory;
        private IIsAssignedInlay<T>? _inlay;
        private bool _disposed;

        [Fragment]
        public IsAssignedRegion() { }

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
