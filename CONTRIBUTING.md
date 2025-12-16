# Contributing

Thanks for your interest in contributing and improving this library for everyone!

## How to Contribute

1.  Fork and clone the repo.
2.  Open the `Visual Studio Solution` to install dependencies.
3.  Create a branch for your PR with `git checkout -b pr-type/your-branch-name`.

## Commit Guidelines

Be sure your commit messages follow this specification: https://www.conventionalcommits.org/en/v1.0.0-beta.4/

## Branch Names (pr-type)

- `feat/` - feature
- `fix/` - bug fix
- `wip/` - testing

## Help Patches

If you're adding a brand new feature, you need to make sure you add a help patch entry. Here are a few tips:

- Keep the help patch simple & show the essence of the feature. Remember some people may be looking at using this for the first time & it's important the help patches are clear and concise.
- Keep assets minimal (3D Models, textures) to avoid bloating the repository.
- If you think a more involved example is necessary, you can always add an `Example` while keeping the `Reference` minimalistic. See [providing-help](https://thegraybook.vvvv.org/reference/extending/providing-help.html).

## Releasing

Releases are currently triggered manually with the following convention:

- `fix:` will create a `0.0.x` version
- `feat:` will create a `0.x.0` version
- `BREAKING CHANGE:` will create a `x.0.0` version

## Deployment Workflows

Workflows are triggered with the `draft new release` action, creating a tag:

- `vX.X.X`: stable release
- `vX.X.X-(pre|rc|etc.)`: prerelease
    
Appearance of tag `v*` should trigger the action.

