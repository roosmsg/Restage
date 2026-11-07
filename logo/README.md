# Restage final logo assets

This package contains the approved indigo-purple / mint-teal Restage symbol.

## Files

- `Restage-transparent-1024.png`: the approved artwork with the presentation background removed. Square canvas, transparent background.
- `Restage.ico`: Windows icon with 16, 20, 24, 32, 40, 48, 64, 96, 128, and 256 px images.
- `Restage.svg`: genuine, editable vector recreation with straight geometry and gradient fills; its gradients and details are approximated from the approved raster artwork.
- `Restage-GitHub-background.png`: original approved artwork with dark presentation background, suitable for README display.
- `Restage-256.png`, `Restage-128.png`, etc.: transparent resized PNG icons.
- `Restage-vector-preview.png`: rendered SVG for comparing the vector recreation to the original.
- `Restage-preview.png`: visual check of the logo and icon sizes.
- `repo-ready/src/Restage.App/Assets/`: drop-in replacements for the project's existing icon-related files. The package does not modify your GitHub repository.

## Integrating into Restage (WPF)

Copy the files from `repo-ready/src/Restage.App/Assets/` to:
`src/Restage.App/Assets/`

The project already has:
`<ApplicationIcon>Assets\Restage.ico</ApplicationIcon>`
and includes `Restage-256.png` as a resource, so no project-file edit is needed.

Rebuild your application, and if Windows shows an old taskbar icon, unpin the old shortcut, restart Explorer or refresh the icon cache, and repin the new build.

For your GitHub README, add an image:
`![Restage logo](docs/assets/Restage-GitHub-background.png)`
(after copying the image to that repository path).

The `Restage.svg` is a clean editable redraw. For pixel-for-pixel appearance, use `Restage-transparent-1024.png` or the original background PNG instead.
