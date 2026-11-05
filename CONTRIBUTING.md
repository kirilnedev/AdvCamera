# Contributing to AdvCamera

Thanks for your interest! Bug reports, feature ideas and pull requests are all welcome.

## Reporting a bug

Open an [issue](https://github.com/kirilnedev/AdvCamera/issues/new/choose) and include:

- Windows version
- Camera model (the name shown in AdvCamera's camera list)
- The resolution / format you selected
- What you did, what you expected, and what happened instead

## Making a change

1. Fork the repository and create a branch from `main` (e.g. `fix/recording-fps`).
2. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
3. Build and run: `dotnet run --project src/AdvCamera`
4. Keep the change focused. One pull request per feature or fix is easiest to review.
5. Make sure `dotnet build -c Release` succeeds without new warnings.
6. Open a pull request against `main` and describe what you changed and how you tested it
   (which camera, which settings).

All pull requests are built automatically by GitHub Actions and need a passing build and an approving review
before they can be merged. Nobody pushes to `main` directly.

## Code style

- Follow the existing style; `.editorconfig` covers the basics.
- Image processing goes in `Processing/`, camera access in `Camera/`, UI in the XAML files.
- The capture loop runs on a background thread: never touch WPF objects from it, and keep per-frame work fast.

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
