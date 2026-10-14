# ModIA — Companheiro NPC com IA para Valheim

Mod de **companheiro NPC com inteligência artificial** para **Valheim 1.0** (inspirado no "Thrall",
recriado do zero para a versão atual). Projeto **pessoal de estudo** — ver [`NOTICE.md`](NOTICE.md).

> **Status:** 🚧 em desenvolvimento — Fase 2 (núcleo do NPC). O companheiro já **spawna, segue,
> equipa itens, tem aparência de viking, combate, coleta recursos** (madeira, pedra, minérios e
> colhíveis — ver a [tabela de materiais](#materiais-suportados-fase-28)), **abre portas e evita
> barrancos no caminho**, mostra **nome + teclas no
> hover**, tem um **menu no jogo** (status, aparência, combate, coleta e teclas) e **volta o mesmo**
> depois de dispensado (itens, equipamento, aparência e modo). Ainda faltam **multiplayer**,
> **voz/IA** e **backend**.

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
| `modia_gather <recurso> [qtd]` | Faz o companheiro coletar um recurso (ex.: `modia_gather wood 20`, `modia_gather pedra`, `modia_gather cobre 30`) — ver a [lista de materiais](#materiais-suportados-fase-28) |
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
| **Coleta** | **Recurso** (dropdown com os materiais conhecidos — ver a [tabela](#materiais-suportados-fase-28) — mais **Outro…** para digitar qualquer item), **quantidade** (0 = sem limite) e botões **Iniciar** / **Parar** |
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

### Coleta de recursos (Fase 2.4b · ampliada na 2.8 · corrigida na 2.9)

O companheiro procura nós de coleta num raio (padrão 40 m), escolhe o de maior **valor esperado**
(unidades do recurso pedido que o nó rende × esforço em golpes × distância), equipa a **ferramenta
adequada** (machado para madeira, picareta para pedra/minério — escolhida pelas resistências do nó),
bate até destruir, **pega os drops** e segue para o próximo nó, repetindo até a **quantidade** pedida
ou até não haver mais nó no raio.

- **Madeira sai de árvore**: o companheiro derruba a árvore (`TreeBase`) e **mira na hora a tora mais
  próxima** (`TreeLog`, raio 12 m) — é nela que a madeira está (uma Beech1 rende ~20 Wood) — depois
  parte as **toras menores** que ela gera (`m_subLogPrefab`). A cascata
  `m_logPrefab` → `m_subLogPrefab` é o que faz a árvore ganhar dos galhos (`Pickable`) e dos itens
  avulsos no chão; esses continuam valendo como fonte quando não há árvore por perto.
- **Drops têm vez**: ao destruir um nó o companheiro **espera 0,5 s** (os drops levam alguns frames
  para nascer), varre o ponto de destruição (raio de 8 m) e pega os itens um por um — a tarefa
  **não** encerra mais por engano em ~1,5 s. A varredura de drops termina por **5 varreduras vazias
  ou 5 s** e só então ele volta a procurar nós.
- **Pedra e minério saem de rocha**: `MineRock`/`MineRock5` dropam por **área de golpe**, então o
  companheiro **mira na superfície** da rocha (não no centro da raiz) e reposiciona quando uma área
  some — é o que faz os golpes conectarem em rochas grandes. Quando o jogo troca a rocha por uma
  **cópia "rachada" no mesmo ponto** (`Destructible.m_spawnWhenDamaged`), ele continua na cópia em
  vez de largar o nó (antes era "um golpe por pedra").
- **Ferramenta**: quando falta machado ou picareta, o mod **fornece uma** do conjunto
  `Coleta/FerramentasFornecidas` (padrão `AxeStone,PickaxeAntler,PickaxeIron`) e coloca no inventário
  do companheiro — sem isso o loadout padrão (`Club`) não arranha pedra nenhuma. Se o nó escolhido
  exigir **tier maior** (prata/obsidiana), o mod fornece a **melhor ferramenta da lista que atenda**
  (tier ≥ mínimo do nó e dano > 0 contra as resistências dele). Desligue com
  `Coleta/FornecerFerramentas = false`; sem ferramenta capaz, o HUD avisa e o nó **não** vira alvo
  (o companheiro não fica mais batendo à toa até travar).
- **Recursos**: a lista curada da tabela abaixo resolve `modia_gather pedra`, `stone`, `Stone`,
  `madeira`, `silex`, `cobre`… Qualquer **item** do jogo continua valendo pelo nome do prefab
  (`Wood`, `Stone`, `Raspberry`, `Resin`…) ou pelo **nome localizado** do item. `modia_gather` sem
  argumento mostra a lista.
- Quantidade: `modia_gather wood 20` para parar em 20; sem quantidade ele coleta enquanto achar nó.
- Combate x coleta: nos modos **Defensive/Aggressive** um inimigo hostil **pausa** a coleta (a
  tarefa continua e é retomada quando o combate acaba); no **Passive** ele ignora inimigos e
  continua coletando. Desligue com `Coleta/PausarEmCombate = false`.
- Segurança: nunca ataca **construções do jogador** (peças/`WearNTear`) nem **contêineres**, não
  coleta nós submersos nem nós encostados em construções, e larga o nó se o jogador se afastar.
- Com `Geral/LogDetalhado = true` cada varredura imprime um diagnóstico (nós por categoria,
  candidatos que casam com o recurso, motivos de descarte — inclusive `sem ferramenta` — e o alvo
  escolhido com camada, rendimento estimado, golpes e score) — é a ferramenta para investigar "por
  que ele não pegou X".

#### Materiais suportados (Fase 2.8)

O que cai de **nó de coleta** (árvore/tora, pedra/minério, planta colhível) o companheiro coleta. A
coluna **Ferramenta** é o que o jogo exige para arranhar o nó (`HitData.CheckToolTier`); sem ela o
mod **fornece** a ferramenta (config acima) ou avisa no HUD.

| Material (item) | Categoria | De onde vem (nó) | Ferramenta | Situação |
|---|---|---|---|---|
| `Wood` (Madeira) | madeira | árvores (`TreeBase`) → toras (`TreeLog`), tocos | machado (corte) | ✅ funciona |
| `FineWood` (Madeira fina) | madeira | carvalho/bétula | machado | ✅ funciona |
| `RoundLog` (Tora) | madeira | toras no chão | machado | ✅ funciona |
| `ElderBark` (Casca de ancião) | madeira | árvores anciãs (pântano) | machado | ✅ funciona |
| `YggdrasilWood` (Madeira de Yggdrasil) | madeira | brotos de Yggdrasil (Mistlands) | machado | ✅ funciona |
| `Blackwood` (Madeira negra) | madeira | árvores de Ashlands | machado | ✅ funciona |
| `Resin` (Resina) | madeira | drop de árvore (faia/pinheiro/abeto) | — | ✅ funciona |
| `Stone` (Pedra) | pedra | pedrinhas (`Pickable`) e rochas grandes (`MineRock`/`MineRock5`) | picareta (tier 1) | ✅ funciona |
| `Flint` (Sílex) | pedra | pedras de praia (`Pickable`) | — | ✅ funciona |
| `Obsidian` (Obsidiana) | pedra | veios da montanha (`MineRock5`) | picareta (tier alto) | ✅ funciona (fornece a picareta do tier) |
| `BlackMarble` (Mármore negro) | pedra | depósitos da Mistlands | picareta | ✅ funciona |
| `Grausten` (Grausten) | pedra | depósitos de Ashlands | picareta | ✅ funciona |
| `CopperOre` (Minério de cobre) | minério | depósito de cobre (`MineRock5`) | picareta (tier 1) | ✅ funciona |
| `TinOre` (Minério de estanho) | minério | depósito de estanho (`MineRock5`) | picareta (tier 1) | ✅ funciona |
| `IronScrap` (Sucata de ferro) | minério | pilhas de sucata do pântano (`MineRock`) | picareta (tier 1+) | ✅ funciona |
| `SilverOre` (Minério de prata) | minério | veio de prata (`MineRock5`, precisa de *wishbone* p/ achar) | picareta (ferro ou melhor) | ✅ funciona (fornece `PickaxeIron` pelo tier) |
| `BlackMetalScrap` (Sucata de metal negro) | minério | depósito/campo de Fulings | picareta (metal negro) | ⚠️ depende de haver nó (ver ressalvas) |
| `CopperScrap` (Sucata de cobre) | minério | depósitos (Mistlands/Ashlands) | picareta | ⚠️ depende de haver nó (ver ressalvas) |
| `FlametalOreNew` (Minério de flametal) | minério | depósitos de Ashlands | picareta (tier alto) | ⚠️ depende de haver nó (ver ressalvas) |
| `Chitin` (Quitina) | minério | "barnacles" do Leviatã (mar) | picareta | ⚠️ depende de haver nó (ver ressalvas) |
| `Crystal` (Cristal) | minério | cristais da montanha | picareta | ⚠️ depende de haver nó (ver ressalvas) |
| `Raspberry` (Framboesa) | colhível | arbusto (`Pickable`) | — | ✅ funciona |
| `Blueberries` (Mirtilos) | colhível | arbusto (`Pickable`) | — | ✅ funciona |
| `Cloudberry` (Amora-branca) | colhível | arbusto (`Pickable`) | — | ✅ funciona |
| `Mushroom` (Cogumelo) | colhível | `Pickable` | — | ✅ funciona |
| `MushroomYellow` (Cogumelo amarelo) | colhível | `Pickable` | — | ✅ funciona |
| `MushroomBlue` (Cogumelo azul) | colhível | `Pickable` | — | ✅ funciona |
| `MushroomMagecap` (Cogumelo mágico) | colhível | `Pickable` | — | ✅ funciona |
| `MushroomJotunPuffs` (Cogumelo Jotun) | colhível | `Pickable` | — | ✅ funciona |
| `MushroomSmokePuff` (Cogumelo de fumaça) | colhível | `Pickable` | — | ✅ funciona |
| `Thistle` (Cardo) | colhível | `Pickable` | — | ✅ funciona |
| `Dandelion` (Dente-de-leão) | colhível | `Pickable` | — | ✅ funciona |
| `Carrot` (Cenoura) | colhível | `Pickable` (plantação) | — | ✅ funciona |
| `Turnip` (Nabo) | colhível | `Pickable` (plantação) | — | ✅ funciona |
| `Onion` (Cebola) | colhível | `Pickable` (plantação) | — | ✅ funciona |
| `Barley` (Cevada) | colhível | `Pickable` (plantação nas planícies) | — | ✅ funciona |
| `Flax` (Linho) | colhível | `Pickable` (plantação nas planícies) | — | ✅ funciona |
| `RoyalJelly` (Geleia real) | colhível | `Pickable` (Mistlands) | — | ✅ funciona |
| `Honey` (Mel) | colhível | colmeia (`Pickable`/baú) | — | ✅ funciona |

**Fora do escopo** (não há nó de coleta para o companheiro procurar — vêm de criatura, baú ou
produção do jogador):

| Material | Por que não |
|---|---|
| `Feathers` (Penas), `LeatherScraps` (Retalhos de couro), `DeerHide` (Pele de cervo), `BoneFragments` (Fragmentos de osso), `GreydwarfEye` (Olho de Greydwarf), `Guck` (Gosma), `TrollHide` | só caem de **criatura** (`CharacterDrop`) — a coleta por combate é outra fase |
| `Coal` (Carvão) | produção do jogador (fornalha/kiln) ou drop de Surtling |
| `Amber`, `AmberPearl`, `Ruby`, `SilverNecklace`, `Gold` | **baú / tesouro enterrado** (contêiner), não nó |
| Peixe (`Fish*`) | pesca (a Fase de coleta não cobre a vara) |

> **Ressalvas.** Os nomes de prefab da tabela são os do Valheim 1.0. Os itens marcados com ⚠️
> (`BlackMetalScrap`, `CopperScrap`, `FlametalOreNew`, `Chitin`, `Crystal`) estão na lista curada e o
> scanner os resolve pelo **componente do nó** (como qualquer outro material), mas a **fonte exata**
> não pôde ser confirmada só pelo código do jogo (o nó é um asset do bundle). Se o item vier só de
> **criatura/baú** no seu mundo, a varredura simplesmente não encontra nó e o log diz isso
> (`nenhum nó de coleta no raio`); não há falso positivo. Confirme com `Geral/LogDetalhado = true`.

### Movimento e combate (Fase 2.10 · correções da auditoria)

Correções dos itens #2, #5, #6 e #14 da
[auditoria comparativa](docs/audit-vs-offline-companions.md) (vs Offline Companions Reborn, MIT):

- **Não cai de barranco (#2).** Antes de cada passo — na **coleta** e no **follow** — o companheiro
  sonda o chão logo à frente (um *spherecast* curto com a altura do `ZoneSystem` como reserva). Se o
  chão **cair mais que `Movimento/QuedaMaxima`** (padrão **1,2 m**) ele tenta **desviar** para um
  lado seguro; sem desvio, **para** em vez de se jogar. Cair na **água funda** não conta como
  barranco. Se ele estiver parando em rampas legítimas, aumente `QuedaMaxima` ou reduza
  `DistanciaSondagem`.
- **Abre portas no caminho (#5).** Uma porta **fechada e utilizável** logo à frente (raycast curto por
  `Door`) é aberta (`Door.Interact`) e o companheiro passa; depois de passar, **fecha de novo**
  (`Movimento/AbrirPortas`, padrão `true`). Nunca abre portas com **item de chave** (`m_keyItem`) nem
  portas em **área privada sem acesso** (`PrivateArea.CheckAccess`) — e não fecha se o jogador (ou
  alguém) ainda estiver na soleira.
- **Bloqueia/aparar golpes (#6).** Quando um inimigo hostil a menos de `Combate/RaioBloqueio`
  (padrão **8 m**) está atacando, o companheiro vira para ele e **ergue o escudo**
  (`Character.m_blocking` + `Humanoid.m_blockTimer`) por `Combate/TempoBloqueio` (padrão **0,2 s**);
  depois baixa o escudo para **revidar**. Desligue com `Combate/BloquearAtaques = false`. Só tem
  efeito prático se ele estiver com um **escudo equipado** (`modia_equip ShieldWood`, por exemplo).
- **Não caça bicho passivo (#14).** Fora do combate, o companheiro **ignora a fauna passiva/neutra**:
  `AnimalAI` (cervo, lebre, galinha) e criaturas que só revidam (`m_passiveAggresive`), fogem
  (`m_fleeIfNotAlerted`) ou vivem na água (`m_avoidLand`). Quem já está **atacando/alertado** continua
  sendo alvo válido (o revide do modo Defensive não muda). Para caçar essa fauna mesmo assim, ligue
  `Combate/AtacarAnimaisNeutros = true`.

> **Um dono do movimento por frame.** Para porta, barranco, coleta e follow não disputarem o mesmo
> `MoveTo`, o mod define **um dono por tick**: a **coleta** (quando ativa e dirigindo) tem prioridade
> sobre o **follow/combate nativo** do motor do jogo; o **combate** vence a coleta **pausando-a**
> antes (o alvo hostil continua com a prioridade de sempre). As guardas de porta/barranco só agem
> quando o dono do frame é o follow nativo — nunca brigam com a coleta.

### Configuração (seções do `.cfg`)

- **Geral** — `LogDetalhado`
- **Teclas** — `SpawnKey` (F1), `DismissKey` (H), `CombatModeKey` (F3), `HarvestKey` (F4), `MenuKey` (Y)
- **Companheiro** — `EquiparLoadoutNoSpawn`, `WeaponPrefab`, `ArmorSetPrefabs`, `PersistirEntreSessoes`
- **Aparencia** — `AparenciaPadrao`, `CabeloPrefab`, `BarbaPrefab`, `CorPele`, `CorCabelo`
- **Combate** — `Modo`, `RaioAgressivo`, `RaioDefensivo`, `Coleira`, `AtacarAnimaisNeutros`, `IntervaloVarredura`,
  `BloquearAtaques`, `RaioBloqueio`, `TempoBloqueio`
- **Movimento** — `ProtecaoContraQueda`, `QuedaMaxima`, `DistanciaSondagem`, `AbrirPortas`
- **Coleta** — `RecursoPadrao`, `QuantidadePadrao`, `RaioVarredura`, `IntervaloVarredura`,
  `PausarEmCombate`, `CatarDrops`, `Camadas`, `FornecerFerramentas`, `FerramentasFornecidas`
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

### Teste rápido da coleta ampliada (Fase 2.8)

1. **F1** invoca o companheiro e **F4** (ou `modia_gather wood 20`) coleta madeira como antes.
2. **Pedra**: vá para perto de rochas/pedrinhas e rode `modia_gather pedra 30`. O log deve mostrar
   `alvo '...' (MineRock/MineRock5/Pickable ...)`, e a mensagem de HUD `Coleta: Stone (30x)`.
   Se faltar ferramenta, o mod cria a picareta (`Coleta/FornecerFerramentas`) e o log registra
   `Ferramenta de coleta fornecida ... 'PickaxeAntler'`.
3. **Minério**: `modia_gather cobre 20` (perto de um depósito de cobre) e `modia_gather estanho 20`.
   Para prata/obsidiana (tier 2) o padrão já inclui `PickaxeIron` (ver o teste da 2.9 abaixo).
4. **Menu**: **Y** com o companheiro a até 5 m → seção **Coleta** → escolha o recurso no **dropdown**
   (ex.: "Pedra", "Sílex", "Minério de cobre"), a quantidade e **Iniciar**. A opção **Outro…**
   revela o campo livre para digitar qualquer item.
5. **Diagnóstico**: `Geral/LogDetalhado = true` mostra, por varredura, quantos nós casaram, quantos
   ficaram `sem ferramenta` e qual foi o escolhido (com rendimento/golpes/score).
6. Para **spawnar** pedra/minério perto de você (para o teste): console (**F5**) do jogo —
   `spawn Pickable_Stone 10`, `spawn MineRock 3` (ou `spawn Rock_3 3`) e
   `spawn Pickable_Flint 5`; para minério, `spawn MineRock_Copper 1` / `spawn MineRock_Tin 1`.

### Teste rápido das correções de coleta (Fase 2.9)

1. **Árvore (fim do encerramento precoce)**: `modia_gather wood 20` numa floresta. Confira no log:
   `alvo 'Beech1' (TreeBase …)` → depois de derrubar, `árvore caiu; mirando a tora mais próxima
   'log'` (ou `'log_half'` para a tora partida) → ao quebrar a tora, `fim da coleta de drops (…)`.
   A coleta deve durar até a **quantidade** ou até não haver mais nó — não pode parar com "nada foi
   coletado" logo após o primeiro tombo.
2. **Rocha "rachada"**: `modia_gather pedra 30` perto de pedras. O log deve mostrar
   `'MineRock' trocado por uma cópia no mesmo ponto; continuando em '…'` e o companheiro **continua
   batendo na mesma pedra** (antes dava um golpe por pedra e saía andando).
3. **Drops**: com `Coleta/CatarDrops = true` (padrão), depois de destruir o nó o companheiro espera
   ~0,5 s e vai atrás das madeiras/pedras no chão (`+Nx 'Wood' (…)` no log). Com
   `Geral/LogDetalhado = true` dá para ver as varreduras de drops.
4. **Prata com `PickaxeIron`**: vá para uma montanha, ative o *wishbone* e `modia_gather prata 20`
   (ou `spawn MineRock_Silver 1` no console, se preferir). Com o padrão
   `Coleta/FerramentasFornecidas = AxeStone,PickaxeAntler,PickaxeIron` o log deve registrar
   `Ferramenta de coleta fornecida pelo tier: 'PickaxeIron' (tier 2, mínimo do nó 2)` e o alvo passa
   a ser o veio — `modia_inv` mostra a picareta de ferro. Repita com `modia_gather obsidiana 10`.
5. **Combate x coleta** continua valendo: em **Defensive/Aggressive** um inimigo pausa a coleta (a
   espera pelos drops é retomada depois); em **Passive** ele ignora os inimigos.

### Teste rápido do movimento e combate (Fase 2.10)

1. **Barranco (#2)**: suba a um penhasco/margem alta com o companheiro seguindo. Ele **não deve
   pular** atrás de você: para na beira ou contorna. Com `Geral/LogDetalhado`/observação, ligue
   `Movimento/ProtecaoContraQueda = false` e repita para comparar. Se ele parar em **rampas
   suaves**, aumente `Movimento/QuedaMaxima` (ex.: 2,0).
2. **Portas (#5)**: construa/entre numa casa com porta (ou use uma cabana do mundo) e ande para
   dentro com o companheiro seguindo — ele **abre a porta** e, depois de passar, **fecha**. Portas
   trancadas (com chave) e de bases alheias **não** devem abrir.
3. **Bloqueio (#6)**: equipe um escudo no companheiro (`modia_equip ShieldWood`), provoque um
   inimigo (Greydwarf/Skeleton) e observe: quando ele ataca, o companheiro **ergue o escudo** por um
   instante e **revida** logo depois. Desligue `Combate/BloquearAtaques` para comparar.
4. **Alvo passivo (#14)**: em modo **Aggressive** (`F3`), ande por uma campina com **cervos/javalis**:
   com `Combate/AtacarAnimaisNeutros = false` (padrão) ele **ignora** os passivos e só ataca hostis
   (Greydwarf, esqueleto…). Ligue `AtacarAnimaisNeutros = true` para ele caçar a fauna.

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
- [`docs/references.md`](docs/references.md) — **fontes de terceiros e licenças** (inclui o mod **MIT**
  [Offline Companions Reborn](https://github.com/MaddhattrRocks/Offline-Companions-Reborn), usado como referência).
- [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) — avisos de licença de terceiros.
- [`AGENTS.md`](AGENTS.md) — como a equipe de agentes trabalha neste repositório.

---

## Licença / uso

Projeto **pessoal de estudo**. Não afiliado à Iron Gate, Coffee Stain ou ego.ai. Nenhum binário ou
código de terceiros é redistribuído. Ver [`NOTICE.md`](NOTICE.md).
