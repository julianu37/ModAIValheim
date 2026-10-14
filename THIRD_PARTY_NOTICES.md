# Avisos de terceiros (Third-party notices)

Este projeto pode conter código ou ideias derivados de projetos de terceiros. Os avisos de licença
exigidos estão abaixo. Ver também [`NOTICE.md`](NOTICE.md) e [`docs/references.md`](docs/references.md).

---

## Offline Companions Reborn — Licença MIT

Repositório: <https://github.com/MaddhattrRocks/Offline-Companions-Reborn>

```
MIT License

Copyright (c) ProfMags (original "Offline Companions" mod)
Copyright (c) 2026 Maddhattr (Valheim 1.0 compatibility fork, "Offline Companions - Reborn")

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

> Se qualquer trecho de código derivado deste projeto for incorporado ao ModIA, **este aviso deve
> permanecer** (obrigação da licença MIT).
>
> Uso no ModIA (a licença MIT é mantida mesmo quando só a *lógica* é reaproveitada):
>
> - **Fase 2.9 (v0.3.2)** — as correções de coleta seguem a lógica de
>   `src/Companions/Jobs/HarvestController.cs` (`UpdateCollectingDrops`, `_dropScanDelayTimer`,
>   `TryFollowCrackedRock`) e de `src/Companions/Jobs/MHR_GatherRules.cs` (`NearestLog`,
>   `DropCenter`). A implementação no ModIA é própria (adaptada ao modelo de estados do
>   `CompanionAI` e aos buffers reaproveitados do `CompanionHarvestScanner`), sem cópia de arquivo.
> - **Fase 2.10 (v0.3.3)** — as correções de movimento/combate seguem a lógica de
>   `src/Companions/AI/MHR_Mover.cs` (`LedgeAhead`, `DropAhead`, `DoorAhead`, `OpenDoor`,
>   `CloseDoor`), `src/Companions/AI/DoorHandler.cs` (`IsValidClosedDoor`) e
>   `src/Companions/AI/CompanionCombatAI.cs` (`UpdateBlocking`). A implementação no ModIA é própria
>   (`CompanionMovement` + o dono do movimento por frame do `CompanionAI`), sem cópia de arquivo.
> - **Fase 2.12 (v0.4.0)** — o **loot de criatura** segue a lógica de
>   `src/Companions/Jobs/MHR_Loot.cs` (`MHR_Loot` + `MHR_Loot_OnDeath_Patch`): patch observador em
>   `Character.OnDeath` para saber quem deu o último golpe, fila limitada de abates, foto dos itens
>   que já estavam no chão (`Before`) para catar só o que aquele abate gerou, espera pelos drops que
>   saem do ragdoll, acompanhamento do corpo e limites de peso/distância/tempo. A implementação no
>   ModIA é própria (`CompanionLoot` + `CompanionLootPatches` + `CompanionAI.UpdateLoot`, adaptados ao
>   modelo de estados, ao dono do movimento por frame e aos buffers reaproveitados), sem cópia de
>   arquivo.
