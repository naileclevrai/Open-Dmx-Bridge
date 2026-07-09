# OpenDMX Bridge

Remplaçant moderne d'ArtNetToDMX : conversion **Art-Net** (UDP 6454) vers sortie **OpenDMX** (FTDI FTD2XX).

## Stack

- **C# / .NET 8 LTS** (Windows x64)
- **WPF** + **MVVM** (CommunityToolkit.Mvvm)
- Architecture orientée services (UI isolée du réseau et du DMX)

## Architecture

```
UI (WPF / MVVM)
 │
 ├── Settings Service
 ├── Network Service (Art-Net UDP 6454)
 ├── DMX Engine (ConcurrentDictionary<UniverseId, UniverseBuffer>)
 ├── IDmxOutput (plugins)
 │    ├── OpenDMXOutput (FTD2XX, timings DMX512)
 │    ├── NullOutput (mode Monitor)
 │    ├── EnttecProOutput / DMXKingOutput / sACN / ArtNet (stubs)
 ├── Logging Service (TRACE…ERROR + export .log)
 └── Bridge Orchestrator
```

### Fiabilité

- **Double buffer atomique** (`Interlocked.Exchange`) entre thread réseau et moteur DMX
- **Horloge précise** Stopwatch avec correction de dérive (~44 Hz)
- **Timings OpenDMX** : break/MAB réglables — validation oscilloscope requise ([docs/DMX_TIMING.md](docs/DMX_TIMING.md))
- **FTD2XX.dll** chargée dynamiquement (`NativeLibrary`) — démarrage sans DLL, mode Monitor OK
- **Reconnexion USB** automatique sans fermeture de l'application
- **Zéro allocation** dans les boucles réseau et DMX (buffer pré-alloué, pas de LINQ)

### Monitoring

- FPS Art-Net et DMX
- Délai depuis dernier paquet (timeout > 2 s → LED rouge)
- Séquences perdues et paquets hors ordre
- Mode **Monitor** : analyse sans sortie physique

## Fonctionnalités MVP

- Réception Art-Net (ArtDMX 0x5000)
- Sélection carte réseau (bind sur IP locale)
- Filtrage Net / SubNet / Universe
- Compteurs paquets reçus / envoyés / invalides
- Monitoring 512 canaux temps réel
- Sortie OpenDMX (~44 Hz configurable)
- Détection automatique FTDI + reconnexion
- Journal applicatif
- Arrêt propre des threads

## Prérequis

1. **Windows 10/11 x64**
2. [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
3. **Pilote FTDI D2XX** + `FTD2XX.dll` (x64) — fourni avec le [driver FTDI](https://ftdichip.com/drivers/d2xx-drivers/)
4. Interface **Enttec Open DMX USB** (ou compatible FTDI OpenDMX)

> Copiez `FTD2XX.dll` (64 bits) à côté de `OpenDMXBridge.exe` si elle n'est pas déjà dans le PATH système.

## Build

```powershell
dotnet build src/OpenDMXBridge/OpenDMXBridge.csproj -c Release
```

Binaire : `src/OpenDMXBridge/bin/Release/net8.0-windows/OpenDMXBridge.exe`

## Utilisation

1. Brancher l'interface OpenDMX USB
2. Lancer l'application
3. Choisir la carte réseau utilisée par votre console / logiciel lumière
4. Régler l'univers Art-Net cible (`Net.SubNet.Universe`)
5. Cliquer **Démarrer**

L'indicateur Art-Net passe au vert lorsque des paquets sont reçus (dernières 2 secondes).

## Évolutivité prévue

- sACN (E1.31), Enttec USB Pro, DMXKing
- Multi-univers, merge HTP/LTP, RDM
- Enregistrement / lecture, moniteur Art-Net
- Mapping univers, API locale

## License

This project is licensed under the Apache License 2.0.
See the LICENSE file for details.
