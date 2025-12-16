using VL.Core.Import;

namespace VL.CoreLib.Addons.Primitives.StetefulDelegate
{
    /// <summary>
    /// Helper class that manages the lifecycle of a stateful operation.
    /// It handles lazy initialization of state, updating state with input, and disposing of state if necessary.
    /// </summary>
    /// <typeparam name="TState">The type of the internal state.</typeparam>
    /// <typeparam name="TIn">The type of the input data.</typeparam>
    /// <typeparam name="TOut">The type of the output data.</typeparam>
    public class StetefulDelegatePatchInlay<TState, TIn, TOut> : IDisposable
    {
        protected TState State = default!;
        private bool _invalidate = true;

        /// <summary>
        /// Updates the state based on the provided logic and input.
        /// </summary>
        /// <param name="create">Function to create the initial state.</param>
        /// <param name="update">Function to update the state and produce output.</param>
        /// <param name="input">The input value for this update.</param>
        /// <param name="output">The produced output value.</param>
        public void Update(
            Func<TState> create,
            Func<TState, TIn, Tuple<TState, TOut>> update,
            TIn input,
            out TOut output
        )
        {
            if (_invalidate)
            {
                State = create.Invoke();
                _invalidate = false;
            }

            // Invoke returns a System.Tuple object
            var result = update.Invoke(State, input);

            State = result.Item1;
            output = result.Item2;
        }

        public void Dispose()
        {
            if (State is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    /// <summary>
    /// A Process Node that wraps stateful logic defined by delegates.
    /// It maintains state across frames and exposes the logic as a reusable delegate.
    /// </summary>
    /// <typeparam name="TState">The type of the internal state maintained by the node.</typeparam>
    /// <typeparam name="TIn">The type of the input argument.</typeparam>
    /// <typeparam name="TOut">The type of the return value.</typeparam>
    [ProcessNode(FragmentSelection = FragmentSelection.Explicit)]
    public class StatefullDelegatePatch<TState, TIn, TOut> : IDisposable
    {
        private readonly StetefulDelegatePatchInlay<TState, TIn, TOut> _inlay = new();

        // Store the logic delegates so the Output delegate can access them
        private Func<TState> _create = default!;
        private Func<TState, TIn, Tuple<TState, TOut>> _update = default!;

        /// <summary>
        /// Gets a delegate that executes the stateful logic using the current state of this node.
        /// Invoking this delegate will advance the state of this node.
        /// </summary>
        [Fragment]
        public virtual Func<TIn, TOut> Output { get; }

        [Fragment]
        public StatefullDelegatePatch()
        {
            // Initialize the delegate once. It captures 'this' and uses the latest fields.
            Output = input =>
            {
                // Ensure we have logic to run (in case Output is called before Update)
                if (_create is null || _update is null)
                    throw new InvalidOperationException(
                        "Delegate not initialized. Ensure the node is updated at least once."
                    );

                _inlay.Update(_create, _update, input, out var output);
                return output;
            };
        }

        /// <summary>
        /// Updates the node's logic and executes one step of the stateful operation.
        /// </summary>
        /// <param name="create">The delegate used to initialize the state.</param>
        /// <param name="update">The delegate used to update the state and compute the output.</param>
        /// <param name="input">The input value for the current frame.</param>
        [Fragment]
        public virtual void Update(
            Func<TState> create,
            Func<TState, TIn, Tuple<TState, TOut>> update,
            TIn input
        )
        {
            // Capture the logic for the Output delegate to use
            _create = create;
            _update = update;

            // Execute the logic for this frame (standard ProcessNode behavior)
            _inlay.Update(create, update, input, out var _);
        }

        public void Dispose()
        {
            _inlay?.Dispose();
        }
    }
}
