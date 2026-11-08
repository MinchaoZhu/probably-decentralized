# Probably Decentralized

A mod for *Probably Stolen*: crypto mining, a fictional coin market, stablecoins, laundering and its risks, a Web3 black market, built on the game's own power, events and law systems. Current version: 2.0.0.

All coins, exchanges and events are fictional and have no real-world value.

## Layout

| Path | Content |
|---|---|
| `src/Decentralized.Domain` | Pure logic (assets and ledger, AMM pairs, market, mining, graphics card tiers, appearance, labels and serials). No game or Unity references. |
| `src/Decentralized` | The mod: items (trading terminal, mining rig, graphics cards, appraisal guide), used-card dealer, magnifier inspection view with the label bar, nightly settlement, terminal (bank, Poinbase, Web3 with trading, mining, mixer and the black market), black-market shelves, orders, courier and selling, Ponero desk, Web3 handbook, news. Builds into `<game>/Mods/ProbablyDecentralized` (`dotnet build src/Decentralized -p:GameDir=...`). |
| `tests/Decentralized.Domain.Tests` | Console test runner: `dotnet run --project tests/Decentralized.Domain.Tests` |
| `art/sprites.py`, `art/big.py`, `art/big_cases.py` | Generate the item sprites, the large inspection art and its overlays, and the terminal icons into `src/Decentralized/ModFiles/Textures/Items`. |

## Settings

Mod settings are in the game's mod list (select the mod, gear button); they apply to all saves.

| Section | Setting | Values |
|---|---|---|
| Interface | Trading terminal size | 828×680, 1200×900 (default), 1400×1000. Layout and text scale with the window; applies when the terminal is next opened. |
| Story | Tumbler storyline | on / off. Off stops new visits; progress already made is kept. |
| Difficulty | Risk multiplier | ×0.5 / ×1 / ×1.5 / ×2 on the chance of scams, rug pulls, wallet poisoning, address hijacking and phishing. |
| Difficulty | Mining reward multiplier | ×0.5 / ×1 / ×1.5 / ×2 on the block reward paid to the player's rigs. |

## Languages

The interface follows the game language: en, zh, de, fr, es, pl, ru, ja. English is the source text; zh is hand-written; the other six are AI translations and have not been proofread. Tables are in `src/Decentralized/ModFiles/Localization/<code>.csv`.

## License

MIT, see `LICENSE`.

---

《Probably Stolen》的 mod：挖矿、虚构币市场、稳定币、洗钱及其风险、Web3 黑市，接入游戏原有的电力、事件和执法系统。开发中，尚未发布。所有币种、交易所和事件均为虚构，不具有任何现实价值。
