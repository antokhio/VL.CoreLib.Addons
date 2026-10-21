using VL.Core;
using VL.Core.Import;
using VL.Lib.Animation;
using VL.Lib.Basics.Resources;

namespace VL.CoreLib.Addons.Primitives;

/// <summary>
/// The state shared by all <see cref="SingleInstancePerAppStatefullNode{TState, TOut}"/> instances
/// of one app that use the same type arguments and key.
/// </summary>
internal sealed class SingleInstancePerAppStatefullSlot<TState, TOut> : IDisposable
{
    private readonly object _lock = new();
    private readonly IDisposable _ticks;
    private TState _state = default!;
    private bool _created;
    private long _frame;
    private long _lastUpdatedFrame = -1;
    private TOut _lastOutput = default!;

    public SingleInstancePerAppStatefullSlot(IFrameClock frameClock)
    {
        _ticks = frameClock.GetTicks().Subscribe(_ => Interlocked.Increment(ref _frame));
    }

    /// <summary>
    /// Advances the state on the first call within a frame. Subsequent calls within the same frame
    /// return the output of that first call.
    /// </summary>
    public TOut UpdateOncePerFrame(Func<TState> create, Func<TState, Tuple<TState, TOut>> update)
    {
        lock (_lock)
        {
            var frame = Interlocked.Read(ref _frame);
            if (frame != _lastUpdatedFrame)
            {
                _lastOutput = Advance(create, update);
                _lastUpdatedFrame = frame;
            }
            return _lastOutput;
        }
    }

    /// <summary>
    /// Advances the state on every call.
    /// </summary>
    public TOut Invoke(Func<TState> create, Func<TState, Tuple<TState, TOut>> update)
    {
        lock (_lock)
        {
            return Advance(create, update);
        }
    }

    private TOut Advance(Func<TState> create, Func<TState, Tuple<TState, TOut>> update)
    {
        if (!_created)
        {
            _state = create();
            _created = true;
        }

        var result = update(_state);
        _state = result.Item1;
        return result.Item2;
    }

    public void Dispose()
    {
        _ticks.Dispose();
        lock (_lock)
        {
            if (_created && _state is IDisposable disposable)
                disposable.Dispose();

            _state = default!;
            _created = false;
        }
    }
}

/// <summary>
/// A Process Node that wraps stateful logic defined by delegates, where the state exists only once per app.
/// All instances with the same type arguments and key share one state. Only the first <c>Update</c> call
/// within a frame advances the state; all other instances get the cached output of that frame.
/// The state is disposed once the last instance is disposed, or when the app is restarted.
/// </summary>
/// <typeparam name="TState">The type of the internal state shared by all instances.</typeparam>
/// <typeparam name="TOut">The type of the return value.</typeparam>
[ProcessNode(
    Name = "SingleInstancePerApp (Statefull)",
    FragmentSelection = FragmentSelection.Explicit
)]
public class SingleInstancePerAppStatefullNode<TState, TOut> : IDisposable
{
    private IResourceHandle<SingleInstancePerAppStatefullSlot<TState, TOut>>? _handle;
    private Optional<string> _key;

    private Func<TState>? _create;
    private Func<TState, Tuple<TState, TOut>>? _update;

    /// <summary>
    /// Gets a delegate that executes the stateful logic on the shared state.
    /// Every invocation advances the shared state, regardless of the frame.
    /// </summary>
    [Fragment]
    public Func<TOut> Output { get; }

    [Fragment]
    public SingleInstancePerAppStatefullNode()
    {
        Output = () =>
        {
            if (_handle is null || _create is null || _update is null)
                throw new InvalidOperationException(
                    "Delegate not initialized. Ensure the node is updated at least once."
                );

            return _handle.Resource.Invoke(_create, _update);
        };
    }

    /// <summary>
    /// Executes one step of the shared stateful operation, once per frame across all instances.
    /// </summary>
    /// <param name="create">The delegate used to initialize the shared state. The first caller wins.</param>
    /// <param name="update">The delegate used to update the state and compute the output. Only the first caller within a frame executes it.</param>
    /// <param name="result">The output of the current frame.</param>
    /// <param name="key">Optional key to separate multiple shared states with the same type arguments.</param>
    [Fragment]
    public void Update(
        Func<TState> create,
        Func<TState, Tuple<TState, TOut>> update,
        out TOut result,
        Optional<string> key = default
    )
    {
        _create = create;
        _update = update;

        if (_handle is null || !_key.Equals(key))
        {
            _handle?.Dispose();
            _handle = null;
            _handle = ResourceProvider
                .NewPooledPerApp(
                    key,
                    _ => new SingleInstancePerAppStatefullSlot<TState, TOut>(Clocks.FrameClock)
                )
                .GetHandle();
            _key = key;
        }

        result = _handle.Resource.UpdateOncePerFrame(create, update);
    }

    public void Dispose()
    {
        _handle?.Dispose();
        _handle = null;
    }
}
