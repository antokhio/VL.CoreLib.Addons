# VL.CoreLib.Addons

The aim of this library is to bridge the gap between VL's visual patching APIs and C# code APIs.
Currently in [vvvv/vl](https://vvvv.org), some APIs are only available via patches, some are complex to understand, and others are not easily extensible.
While developing libraries, I found that I had to reimplement the same functionality in different ways, so I decided to create a small library to abstract common use case patterns.

## Features

### Stateful Delegates

The `StetefulDelegate` namespace provides components to wrap stateful logic (Create/Update patterns common in VL) into standard .NET `Func<TIn, TOut>` delegates. This enables the usage of stateful process nodes within code-based workflows that expect simple delegates.

#### StatefullDelegatePatch

The `StatefullDelegatePatch<TState, TIn, TOut>` is a Process Node that:

1.  **Maintains State**: Handles the lifecycle of a state object (creation, updates, and disposal).
2.  **Exposes Logic as Delegate**: Provides an `Output` property of type `Func<TIn, TOut>`. Invoking this delegate executes the patch's logic using the internal state.

## Installation

```
nuget install VL.CoreLib.Addons
```

## Notice
This library is in its early stages and may undergo significant changes.


## Contributing
 
[CONTRIBUTING](CONTRIBUTING.md)

## Questions

Issues avalible via [issues][https://github.com/antokhio/VL.Core.Addons/issues].
Questions can be askend on [forum][https://forum.vvvv.org]

Try it with vvvv, the visual live-programming environment for .NET  
Download: http://visualprogramming.net
