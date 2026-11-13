Media used in documentation.

## Animated media targets

The same animation is often made available in multiple aspect ratios and file formats.
This is needed to support their usage in different platforms which have their peculiarities.

| Usage                        | Hosting            | Format | Transparency | Aspect Ratio | Target Resolution | Size Limit          |
| ---------------------------- | ------------------ | ------ | ------------ | ------------ | ----------------- | ------------------- |
| Youtube                      | Youtube            | MP4    | No           | 16:9         | 1920 x 1080       | Unknown             |
| Steam Workshop — Slideshow   | Steam User Content | GIF    | No           | 16:9         | 640 x 360         | <1 MB SDK; 2 MB Web |
| Steam Workshop — Description | GitHub Raw         | WebP   | Yes          | 39:11        | 1170 x 330        | Unknown             |
| README                       | GitHub Raw         | WebP   | Yes          | 39:11        | 1170 x 330        | Unknown             |

The slideshow web uploader allows a larger file size than the SDK. Ask me how I know. 

## Generating GIFs

Create an MP4 at the right aspect ratio, can be higher resolution. 
Use FFMPEG to obtain frames to pass to Gifski which builds a superior color pallete for GIF encoding.

## Generating WebP

In order to preserve transparency WebP should be rendered straight out of your video editor.
Kdenlive is an option.
