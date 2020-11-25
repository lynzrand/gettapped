# ErogePlugins

Various plugins and fixes, for various Unity-based eroges.

## Plugins within this project

### GetTapped

Touch to camera motion adaptor for tablet users.

| Game     | Project | Status |
|----------|---------|---------|
| (Plugin core)|GetTapped.Core|✅
| KoiKatsu | GetTapped.KK| ✅
| Insult Order | GetTapped.IO|✅
| COM3D2 | GetTapped.com3d2 | ✅

### FixKPlug

Performance fix on H-Scene loading for KPlug (KoiKatsu) users

| Game | Project | Status |
|----|----|----|
|KoiKatsu|FixKPlug|✅

### FixEyeMov

Generate fixational eye movement for characters to make them more life-like.

| Game | Project | Status |
|----|----|----|
|(Plugin core)|FixEyeMov.Core |✅
|COM3D2 |FixEyeMov.com3d2|✅

## Before building

Copy the `Assembly-CSharp.dll` of your designated game to `reference/<your-game-name>/`.

If you are going to build FixKPlug, you also need `kPlug.dll` and `ExtensibleSaveFormat.dll` in there.


## Building

- Open project in Visual Studio.
- Hit "Build" really hard.
- Your output should be located in `Release/<project-name>`.
- Install and enjoy!

## License

All plugin mods are licenced under [the MIT license](https://opensource.org/licenses/MIT).

Any other resource (e.g. models, textures, etc.) are licensed under [CC BY-SA 4.0 License](https://creativecommons.org/licenses/by-sa/4.0/).

Copyright (c) 2019-2020 Karenia Works.
