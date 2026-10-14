# ModIA — Companheiro NPC com IA para Valheim

Mod de **companheiro NPC com inteligência artificial** para **Valheim 1.0** (inspirado no "Thrall",
recriado do zero para a versão atual). Projeto **pessoal de estudo** — ver [`NOTICE.md`](NOTICE.md).

> **Status:** 🚧 em desenvolvimento — Fase 2 (núcleo do NPC). O companheiro já **spawna, segue,
> equipa itens, tem aparência de viking, combate, coleta recursos** (madeira, pedra, frutas…),
> mostra **nome + teclas no hover**, tem um **menu no jogo** (status, aparência, combate, coleta e
> teclas) e **volta o mesmo** depois de dispensado (itens, equipamento, aparência e modo). Ainda
> faltam **multiplayer**, **voz/IA** e **backend**.

---

## Versões alvo

| Item | Versão |
|---|---|
| Valheim | 1.0.17 (Unity **6000.0.75f1** / Unity 6) |
| BepInExPack_Valheim | 5.4.2351 (BepInEx 5.4.23.5) |
| Jotunn | 2.30.2 |
| Target do plugin | .NET Framework 4.7.2 (net472) |
| Runtime do "cérebro" (futuro) | Python 3.12 |

---

## 🎮 Controles

| Tecla / comando | Ação |
|---|---|
| **F1** | Invocar o companheiro (spawn) |
| **H** | Dispensar (despawn) |
| **F3** | Ciclar modo de combate: Passive → Defensive → Aggressive |
| **F4** | Iniciar/parar a coleta do **recurso padrão** (config `Coleta/RecursoPadrao`, padrão `Wood`) |
| **Y** | Abrir/fechar o **menu do companheiro** (quando ele estiver a até 5 m) — ver abaixo |
| `modia_gather <recurso> [qtd]` | Faz o companheiro coletar um recurso (ex.: `modia_gather wood 20`, `modia_gather pedra`) |
| `modia_stop` | Para a coleta e volta a seguir o jogador (também limpa o alvo de combate) |
| `modia_state [clear]` | Mostra o companheiro guardado (snapshot); `clear` esquece o snapshot e volta ao loadout padrão |
| `modia_inv` | (console, F5) mostra o inventário/itens equipados do companheiro |
| `modia_equip <prefab> [slot]` | equipa arma/armadura |
| `modia_drop <prefab> [qtd]` | dropa item do companheiro |
| `modia_mode [passive\|defensive\|aggressive]` | define/consulta o modo de combate |

Todas as teclas são configuráveis em `BepInEx\config\modia.companion.cfg`.

### Hover do companheiro (Fase 2.6a)

Com a **mira sobre o companheiro**, o tooltip do jogo (o mesmo que mostra "Madeira" ou "Javali
(dócil)") mostra o nome e o mapa de teclas — tudo lido da configuração, então mudar uma tecla no
`.cfg` muda o que aparece ali:

```
Companheiro
[F3] Modo de combate: Defensivo
[F4] Coleta: Madeira (desligada)
[F1] Invocar / [H] Dispensar
[Y] Menu
```

- A coleta mostra o recurso que o `F4` vai coletar quando está **desligada** e o progresso quando
  está **ligada** (`Coleta: Madeira (ligada, 12/20)`).
- `[Interface] MostrarDicasNoHover = false` deixa só o nome no hover.
- Funciona onde o hover do jogo funciona: mira no companheiro, a até ~5 m (mesma distância de
  interação do jogo). Construções, itens e criaturas continuam com o hover normal.

### Menu do companheiro (Fase 2.6b)

Com o companheiro **a até 5 m**, a tecla **`Y`** (config `Teclas/MenuKey`) abre um **painel de
madeira** no estilo do Valheim, em PT-BR, com cinco seções:

| Seção | O que dá para fazer |
|---|---|
| **Status** | Nome, **vida** atual/máxima, **modo de combate** e **estado da coleta** (recurso + progresso) — só leitura, atualizado 2×/s |
| **Aparência** | Ligar/desligar a aparência personalizada; **cor de pele** e **cor do cabelo/barba** (sliders R/G/B com amostra); **cabelo** e **barba** (lista do jogo + "Padrão do jogo"/"Nenhum") — **aplica em tempo real** |
| **Combate** | Dropdown **Passivo / Defensivo / Agressivo** |
| **Coleta** | **Recurso**, **quantidade** (0 = sem limite) e botões **Iniciar** / **Parar** |
| **Teclas** | Lista **somente leitura** das teclas atuais (invocar, dispensar, modo, coleta, menu) |

- **Fecha** com a mesma tecla, com **ESC** ou pelo botão **Fechar**.
- Com o painel aberto o **input do jogo é bloqueado** (o jogador não anda, o mouse fica livre para
  clicar nos controles) e as demais teclas do mod (spawn/modo/coleta) ficam suspensas. Ao fechar, tudo
  é liberado — inclusive se o companheiro morrer/desaparecer ou o jogador se afastar (o painel fecha
  sozinho, sem deixar o jogador travado).
- As mudanças de aparência, modo e coleta são **gravadas no `.cfg`** (valem para o próximo spawn).
- Enquanto o cursor está em um campo de texto, as teclas são do campo (digitar "y" no recurso não
  fecha o painel).
- O painel é **arrastável** (segure e mova pelo fundo) e o layout é montado **uma única vez** por
  sessão de mundo — não há recriação de UI por frame.

### Persistência do companheiro (Fase 2.7)

**Dispensar (H) não joga nada fora.** Ao dispensar, ao morrer e ao fechar o jogo/o mundo, o
companheiro é **fotografado** (itens, equipamento, aparência e modo de combate) e a próxima
**invocação (F1)** devolve **o mesmo companheiro** — em vez de um NPC vazio com o loadout padrão:

- **Itens**: inventário completo (prefab, quantidade, qualidade, durabilidade, `worldLevel`).
- **Equipamento**: o que estava equipado volta equipado (e o visual também, via `VisEquipment`).
- **Aparência**: cabelo, barba, cor de pele e cor de cabelo/barba.
- **Modo de combate**: Passive/Defensive/Aggressive.
- **Vida**: volta **cheia** (a vida do snapshot é só informativa — dispensar não é morrer).

Onde o estado fica guardado:

| Onde | Sobrevive a | Config |
|---|---|---|
| **Memória** (sempre) | dispensar (H) e trocar de mundo na mesma sessão | — |
| **Disco** (JSON em `BepInEx\config\modia.companion.state.json`) | **fechar e abrir o jogo** | `Companheiro/PersistirEntreSessoes` (padrão `true`) |

- **Sem duplicação**: o companheiro nasce vazio e a restauração só *adiciona* o que estava no
  snapshot; se o inventário já tiver itens, a restauração é recusada (e logada).
- Item cujo **prefab não existe mais** (mod removido, item renomeado pelo jogo) é **pulado com log**,
  e o arquivo tem número de versão de formato (arquivo antigo é ignorado, não corrompe o spawn).
- Na **morte**, os itens ficam guardados (o `CharacterDrop` é removido do prefab, então o corpo não
  dropa nada): re-invocar devolve o companheiro com o que ele tinha.
- O snapshot vale **por instalação** (não por mundo): o companheiro guardado volta igual em qualquer
  mundo. Isso é o que preserva os itens de quem fecha o jogo com o NPC vivo — o prefab é
  **não-persistente**, nada dele vai para o save do mundo.
- Para **esquecer** o companheiro guardado e voltar ao loadout padrão da config: `modia_state clear`
  (ou desligue `PersistirEntreSessoes` para não gravar em disco).
- `modia_state` (console) mostra o que está guardado: itens, equipados, modo, origem e data.

### Coleta de recursos (Fase 2.4b)

O companheiro procura nós de coleta num raio (padrão 40 m), escolhe o de maior **valor esperado**
(unidades do recurso pedido que o nó rende × esforço em golpes × distância), equipa a **ferramenta
adequada** (machado para madeira, picareta para pedra — escolhida pelas resistências do nó), bate
até destruir, **pega os drops** e segue para o próximo nó, repetindo até a **quantidade** pedida ou
até não haver mais nó no raio.

- **Madeira sai de árvore**: o companheiro derruba a árvore (`TreeBase`), depois parte as **toras**
  que caem (`TreeLog`) — é nelas que a madeira está (uma Beech1 rende ~20 Wood). O rendimento é
  estimado seguindo a cascata `m_logPrefab` → `m_subLogPrefab`, então a árvore ganha dos galhos
  (`Pickable`) e dos itens avulsos no chão; esses continuam valendo como fonte quando não há árvore
  por perto.
- Recursos: qualquer **item** do jogo que caia de árvore, tora, pedra, rocha, planta ou entulho
  (`m_dropWhenDestroyed`/`m_dropItems`/`m_itemPrefab`/`DropOnDestroyed`). Vale o nome do prefab
  (`Wood`, `Stone`, `Raspberry`, `Resin`…), apelidos em português (`madeira`, `pedra`, `amora`) e o
  **nome localizado** do item. `modia_gather` sem argumento mostra a ajuda.
- Quantidade: `modia_gather wood 20` para parar em 20; sem quantidade ele coleta enquanto achar nó.
- Combate x coleta: nos modos **Defensive/Aggressive** um inimigo hostil **pausa** a coleta (a
  tarefa continua e é retomada quando o combate acaba); no **Passive** ele ignora inimigos e
  continua coletando. Desligue com `Coleta/PausarEmCombate = false`.
- Segurança: nunca ataca **construções do jogador** (peças/`WearNTear`) nem **contêineres**, não
  coleta nós submersos nem nós encostados em construções, e larga o nó se o jogador se afastar.
- Recursos que só caem de **criaturas** (couro, tripas…) não estão no escopo desta fase.
- Com `Geral/LogDetalhado = true` cada varredura imprime um diagnóstico (nós por categoria,
  candidatos que casam com o recurso, motivos de descarte e o alvo escolhido com camada,
  rendimento estimado, golpes e score) — é a ferramenta para investigar "por que ele não pegou X".

### Configuração (seções do `.cfg`)

- **Geral** — `LogDetalhado`
- **Teclas** — `SpawnKey` (F1), `DismissKey` (H), `CombatModeKey` (F3), `HarvestKey` (F4), `MenuKey` (Y)
- **Companheiro** — `EquiparLoadoutNoSpawn`, `WeaponPrefab`, `ArmorSetPrefabs`, `PersistirEntreSessoes`
- **Aparencia** — `AparenciaPadrao`, `CabeloPrefab`, `BarbaPrefab`, `CorPele`, `CorCabelo`
- **Combate** — `Modo`, `RaioAgressivo`, `RaioDefensivo`, `Coleira`, `AtacarAnimaisNeutros`, `IntervaloVarredura`
- **Coleta** — `RecursoPadrao`, `QuantidadePadrao`, `RaioVarredura`, `IntervaloVarredura`,
  `PausarEmCombate`, `CatarDrops`, `Camadas`
- **Interface** — `MostrarDicasNoHover`

---

## Estrutura do projeto

```
ModIA/
├─ AGENTS.md                  # regras/convenções para os agentes (stack, build, docs)
├─ README.md                  # este arquivo
├─ CHANGELOG.md               # histórico de versões
├─ NOTICE.md                  # aviso de projeto de estudo
├─ Environment.props          # caminhos locais (gitignored)
├─ ModAIValheim.slnx          # solução (VS 2026)
├─ docs/
│  ├─ roadmap.md              # fases e status
│  └─ re/                     # reverse engineering (Fase 1)
├─ src/ModAIValheim/          # código do mod (net472)
│  ├─ Plugin.cs
│  └─ Companion/              # CompanionAI, Prefab, Manager, Inventory, Appearance, Combat, Harvest, Hover, Menu...
└─ .opencode/                 # agentes + skills (equipe de desenvolvimento)
```

---

## Como compilar e testar

```powershell
# Build (copia a DLL para BepInEx\plugins automaticamente)
dotnet build src/ModAIValheim/ModAIValheim.csproj -c Debug

# Teste: iniciar o jogo e conferir o log
#   C:\Program Files (x86)\Steam\steamapps\common\Valheim\BepInEx\LogOutput.log
```

Requisitos: .NET SDK 8+, Valheim 1.0 com BepInEx 5.4.23+ e Jotunn 2.30.2, Visual Studio 2026 (opcional).

### Teste rápido da persistência (Fase 2.7)

1. **F1** invoca o companheiro; `modia_inv` (console, **F5**) mostra o loadout padrão.
2. Faça o companheiro pegar/ganhar itens (ex.: `modia_gather wood 20`), equipe algo
   (`modia_equip AxeStone`) e troque o modo de combate (**F3**).
3. **H** dispensa e **F1** invoca de novo: os itens, o equipamento, o modo e a aparência voltam —
   confira com `modia_inv` e `modia_state` (e a mensagem "itens guardados devolvidos" no HUD).
4. Com `Companheiro/PersistirEntreSessoes = true` (padrão), **feche e abra o jogo** e invoque de
   novo: o companheiro volta igual.
5. Para voltar ao comportamento antigo (loadout padrão), use `modia_state clear`.

---

## Documentação

- [`docs/roadmap.md`](docs/roadmap.md) — fases do projeto e status.
- [`CHANGELOG.md`](CHANGELOG.md) — histórico de versões.
- [`docs/re/`](docs/re/) — **reverse engineering**:
  - [`thrall-features.md`](docs/re/thrall-features.md) — o que o Thrall fazia, por área.
  - [`game-api-usage.md`](docs/re/game-api-usage.md) — APIs do jogo usadas e status na 1.0.17.
  - [`api-diff.md`](docs/re/api-diff.md) — o que quebrou no Valheim 1.0.
  - [`backend-contract.md`](docs/re/backend-contract.md) — endpoints do "cérebro" (LLM/STT/TTS).
  - [`harvesting-notes.md`](docs/re/harvesting-notes.md) — de onde vem a madeira de uma árvore e a fórmula de coleta.
- [`AGENTS.md`](AGENTS.md) — como a equipe de agentes trabalha neste repositório.

---

## Licença / uso

Projeto **pessoal de estudo**. Não afiliado à Iron Gate, Coffee Stain ou ego.ai. Nenhum binário ou
código de terceiros é redistribuído. Ver [`NOTICE.md`](NOTICE.md).
