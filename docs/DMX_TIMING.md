# Validation des timings DMX512 (OpenDMX)

## Contexte

OpenDMX Bridge génère le signal DMX512 via l'API FTDI D2XX :

1. `FT_SetBreakOn` — ligne TX maintenue basse (break)
2. Attente calibrée (QueryPerformanceCounter + spin)
3. `FT_SetBreakOff` — mark after break (MAB)
4. Attente calibrée
5. `FT_Write` — start code `0x00` + 512 slots à **250 kbaud, 8N2**

## Spécification DMX512 (ESTA E1.11)

| Paramètre | Minimum | Valeur par défaut app |
|-----------|---------|------------------------|
| Break     | ≥ 88 µs | 100 µs (réglable)      |
| MAB       | ≥ 8 µs  | 12 µs (réglable)       |
| Baud      | 250 000 | 250 000                |
| Format    | 8N2     | 8N2                    |

## Limitation Windows

Les attentes logicielles (spin sur QPC) mesurent le **temps CPU**, pas directement le niveau électrique sur la ligne DMX. La latence interne du FTDI et du scheduler Windows peut décaler le signal réel.

**La validation en prestation doit se faire à l'oscilloscope ou à l'analyseur logique** sur la sortie XLR (pin 2/3).

## Procédure de mesure recommandée

1. Connecter l'interface OpenDMX à un analyseur (sonde sur Data+, référence Data-).
2. Démarrer le bridge avec un univers actif (tous canaux à 0 sauf un pour repère optionnel).
3. Mesurer sur plusieurs trames :
   - Durée du break (niveau bas avant le start bit)
   - Durée du MAB (niveau haut court)
   - Période bit ≈ 4 µs (250 kbaud)
4. Si break &lt; 88 µs ou MAB &lt; 8 µs : ajuster dans `%LocalAppData%\OpenDMXBridge\settings.json` :

```json
{
  "BreakMicroseconds": 120,
  "MabMicroseconds": 16
}
```

5. Activer les diagnostics logiciels :

```json
{
  "EnableTimingDiagnostics": true
}
```

Puis consulter le journal (niveau TRACE) : les valeurs **logicielles** sont loguées toutes les 1000 trames. Elles indiquent si l'attente CPU atteint la durée demandée — **pas un substitut à l'oscilloscope**.

## FTD2XX.dll absente

L'application charge **dynamiquement** `FTD2XX.dll` au premier accès :

- Démarrage toujours possible
- Bannière + journal si DLL absente
- Mode **Monitor** (Art-Net) pleinement fonctionnel
- Mode **Bridge** OpenDMX bloqué tant que le pilote n'est pas installé

Pilote : [FTDI D2XX Drivers (x64)](https://ftdichip.com/drivers/d2xx-drivers/)
