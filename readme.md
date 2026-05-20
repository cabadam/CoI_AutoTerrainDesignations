# :pick: Automatic Terrain Designations
One Mine — One Click.

## :clipboard: Overview
Kayser’s Automatic Terrain Designations is a quality-of-life mod for Captain of Industry that **generates tailored mining designations for your mine towers**. Instead of manually creating designations across complex ore deposits, the mod analyzes terrain depth and places connected mining orders that follow the contours of the deposit. **Once you play with it, you do not want to go back**.

## :gear: Features
- **Create Designations** — Instantly designate all mineable ore within a mine tower's area
- **Generate Ramps** — Generate access ramps to connect new dig areas to the surface
- **Clear Designations** — Easily clear ATD mining designations within a tower's area
- See **Ore composition** of the designation
- Set **Mining Priority** on tower level
- **Settings** to fine tune the designations
- **Corner Designations** for manual placement
- **Farmland preparation** for preparing flat level designations into farmable ground

## :zap: Quick Start Guide
1. Select a mine tower
2. Ensure the tower area covers a body of some resource
3. Click **Create Designations** to generate designations
4. Adjust settings, clear designation, and recreate until happy
5. Watch your mining crews dig out the entire deposit

Leave a :heart: if you found this mod useful.

---

## Documentation

### Player guides
- [Corner Designations](docs/player/corner-designations.md)
- [Mining Designations](docs/player/mining-designations.md)
- [Farmland preparation](docs/player/farming-designations.md)

### Modder/API docs
- [Mining Designations API](docs/api/mining-designations.md)
- [Farming API](docs/api/farming.md)

### Developer notes
- [Mining Designations Architecture](docs/dev/done/mining-designations.md)
- [Corner Designations Architecture](docs/dev/done/corner-designations.md)
- [Farmland preparation architecture](docs/dev/done/farming-designations.md)
- [Runtime State and Save-Detached Attachments](docs/dev/done/runtime-state.md)

## Installation
- Download the latest version of the mod from GitHub Releases
- Extract the mod folder into your Captain of Industry mods directory (`%AppData%\Captain of Industry\Mods`)
- Enable the mod when loading or starting a new game
- Can be safely removed from saves
- Works with other mods that don't conflict with mining tower inspector

## Build from source
- Install the .NET SDK with .NET Framework 4.8 targeting support
- Make sure Captain of Industry is installed, or set `CAPTAIN_INDUSTRY_MANAGED_PATH` to the game's `Captain of Industry_Data\Managed` directory
- Run `.\build.ps1 -Configuration Release`
- The release zip is created in the project root

## License
MIT. See [LICENSE](LICENSE).

## Attribution and trademarks

Auto Terrain Designations is an unofficial, community-made mod for Captain of Industry.

Captain of Industry, MaFi Games, and related names, trademarks, game code, and assets are the property of MaFi Games. This mod is not affiliated with, endorsed by, or sponsored by MaFi Games.

This repository is intended to contain only original mod code and configuration, licensed under the MIT License. It does not intentionally include Captain of Industry game code, game assets, or other MaFi Games intellectual property. If any such material is found to have been included by mistake, I intend to correct it promptly upon discovery or notice.

## Credits
@moriarty8501 for encouragement, testing, and general support

## User Testimonials
"THIS IS THE BEST DAM MOD EVER >:O"
