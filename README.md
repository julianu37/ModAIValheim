# ModIA — Companheiro NPC com IA para Valheim

Mod de **companheiro NPC com inteligência artificial** para **Valheim 1.0** (inspirado no "Thrall",
recriado do zero para a versão atual). Projeto **pessoal de estudo** — ver [`NOTICE.md`](NOTICE.md).

> **Status:** 🚧 em desenvolvimento — Fase 2 (núcleo do NPC). O companheiro já **spawna, segue,
> equipa itens, tem aparência de viking, combate, coleta recursos** (madeira, pedra, minérios e
> colhíveis — ver a [tabela de materiais](#materiais-suportados-fase-28)) e **cata os drops das
> criaturas que ele mesmo abate** (couro, penas, ossos...), **abre portas e evita
> barrancos no caminho**, mostra **nome + teclas no
> hover**, tem um **menu no jogo** (status, aparência, combate, coleta e teclas) e **volta o mesmo**
> depois de dispensado (itens, equipamento, aparência e modo). Ele também **come sozinho**, tem
> **resistência (stamina)** e **perícias que evoluem**, respeita um **teto de peso** (deposita no baú
> e volta, com *tool keeper*), executa **trabalhos** (`modia_job`: caçar, pescar, cultivar, cozinhar,
> fundir e **cuidar da base**) e tem uma **casa** (`modia_home`), onde **deposita, cuida da
> base** (reparar construções, abastecer fogueiras, esvaziar colmeias e ordenar baús) e para onde
> **volta quando se perde**. Agora ele também **descansa e dorme** (fogueira/cama), **segue você
> pelo portal** e **puxa a sua carroça** — a **qualidade de vida** da Fase 2.18. Ainda faltam
> **multiplayer**, **voz/IA** e **backend**.

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
| `modia_job <hunt\|fish\|farm\|cook\|smelt\|upkeep\|stop> [qtd]` | Manda o companheiro **executar um trabalho** (caçar, pescar, cultivar, cozinhar, fundir, **cuidar da base**) — ver [Trabalhos](#trabalhos-jobs-fase-216) |
| `modia_farm <set\|clear\|info> [raio]` | Define a **zona de fazenda** do trabalho de cultivar no ponto onde você está (raio opcional) |
| `modia_home <set\|clear\|info> [raio]` | Define a **casa/base** do companheiro no ponto onde você está (raio opcional) — ver [Base e casa](#base-e-casa-fase-217) |
| `modia_basecare [on\|off]` | Liga/desliga o **modo contínuo** do cuidado da base (ele cuida da casa sozinho quando fica ocioso) |
| `modia_rest <info\|sit\|sleep\|stop>` | **Descanso/sono** (Fase 2.18): manda ele **sentar no fogo**, **dormir na cama livre**, **levantar** ou mostra o estado — ver [Qualidade de vida](#qualidade-de-vida-fase-218) |
| `modia_cart <info\|pull\|release>` | **Carroça** (Fase 2.18): manda ele **puxar** a carroça livre mais próxima, **soltar** ou mostra o estado |
| `modia_stop` | Para a coleta, o trabalho, o descanso/sono e a carroça, e volta a seguir o jogador (também limpa o alvo de combate) |
| `modia_state [clear]` | Mostra o companheiro guardado (snapshot); `clear` esquece o snapshot e volta ao loadout padrão |
| `modia_inv` | (console, F5) mostra o inventário/itens equipados do companheiro |
| `modia_equip <prefab> [slot]` | equipa arma/armadura |
| `modia_drop <prefab> [qtd]` | dropa item do companheiro |
| `modia_mode [passive\|defensive\|aggressive]` | define/consulta o modo de combate |
| `modia_stats` | (console, F5) mostra comidas ativas, resistência (stamina), perícias, **carga/peso**, depósito, ferramentas emprestadas, o **trabalho em andamento**, a **casa**, o **cuidado da base**, o **descanso/sono**, o **portal** e a **carroça** do companheiro |
| `modia_deposit` | (console, F5) manda o companheiro levar a carga ao **baú** mais próximo agora |

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
| **Status** | Nome, **vida** atual/máxima, **modo de combate**, **comida ativa** (`n/3`) e **resistência** (`atual/máx`) — só leitura, atualizado 2×/s |
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
  (o companheiro não fica mais batendo à toa até travar). Se criar não resolver, o **tool keeper**
  procura a ferramenta num **baú** próximo e vai buscá-la (ver
  [Peso, depósito em baú e tool keeper](#peso-depósito-em-baú-e-tool-keeper-fase-214)).
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
| `Feathers` (Penas), `LeatherScraps` (Retalhos de couro), `DeerHide` (Pele de cervo), `BoneFragments` (Fragmentos de osso), `GreydwarfEye` (Olho de Greydwarf), `Guck` (Gosma), `TrollHide` | não há **nó** para o companheiro procurar — mas desde a **Fase 2.12** ele **cata** o que essas criaturas deixam quando **ele mesmo** as abate (ver [Loot de criatura](#loot-de-criatura-fase-212)) |
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

### Coleta "com coleira" (Fase 2.11 · fim do loop)

A coleta é **serviço, não fuga**: o companheiro só trabalha em nós a até
**`Coleta/RaioVarredura` + 15 m do jogador** (a "coleira", 55 m no padrão). Passando disso ele **larga
o nó e volta a seguir na hora** — e a **mesma** medida é usada na **escolha** do nó, então ele nunca
seleciona algo que largaria logo em seguida. Se existirem nós do recurso, mas **todos fora da
coleira**, a coleta fica **pendente** (não conta como "acabou o recurso") e ele **segue o jogador**;
ela **retoma sozinha** quando você estiver de novo perto de nós. Sem nenhum nó no raio vale o
contrato de sempre: **3 varreduras vazias** encerram a tarefa.

Para ele trabalhar mais longe, aumente `Coleta/RaioVarredura` (a coleira acompanha). Antes da 0.3.4
não existia essa coerência: ele escolhia um nó que a guarda largava no tick seguinte e ficava preso
num ciclo de escolher/largar o mesmo nó, **sem nunca voltar a seguir** (bug do loop da coleta).

### Loot de criatura (Fase 2.12)

O companheiro **cata o que ele mesmo abateu** (item #4 da
[auditoria](docs/audit-vs-offline-companions.md)). Matar um javali e seguir em frente deixando o
couro no chão era o comportamento antigo; agora ele **volta ao corpo**, espera os itens saírem e
pega:

```
[ModIA] Loot: o companheiro abateu 'Javali' (2 item(ns) já estavam no chão ali); 1 abate(s) para catar.
[ModIA] Loot: indo catar o que 'Javali' deixou a 14 m (0 abate(s) ainda na fila).
[ModIA] Loot: +1x 'LeatherScraps'.
[ModIA] Loot: +1x 'BoarTusk'.
[ModIA] Loot: fim do abate de 'Javali' (peguei tudo que caiu).
```

- **Só o que *este* abate gerou.** No instante da morte é fotografado o conjunto de itens que **já
  estava no chão** ali; a varredura ignora essa foto. Um baú de tralha que você largou no chão
  **não** é catado — e item dropado à mão pelo jogador tem `m_autoPickup = false`, então também
  fica onde está.
- **Espera o corpo.** Drops de criatura **não** nascem na morte: o jogo cria um **ragdoll** com a
  lista de drops e são os itens que saem dele quando ele desaparece (alguns segundos depois). Por
  isso o companheiro vai ao ponto do abate, **segue o corpo** se ele escorregar e **espera** até
  `Loot/TempoEspera` (15 s) antes de dizer "nada caiu".
- **Como sabemos que o abate foi dele.** O patch observador em `Character.OnDeath` lê o
  `m_lastHit` do falecido (o **último golpe**, o mesmo campo que o jogo usa para creditar o abate) e
  confere se o atacante é o nosso companheiro. Nenhum estado extra por golpe.
- **Prioridade: combate > coleta > loot/follow.** Com um inimigo hostil ou com uma **tarefa de
  coleta ativa** o abate volta para a **frente da fila** (o relógio dele para junto) e o companheiro
  volta a ele assim que estiver livre. Os itens ficam no chão — nada se perde.
- **Limites (todos configuráveis em `[Loot]`):** raio de 8 m em volta do abate, distância máxima de
  30 m (do companheiro **e** do jogador), fila de 6 abates (ao encher, o mais antigo é abandonado),
  espera de 15 s, teto de 45 s por abate, peso máximo opcional (`0` = sem limite) e guardas de
  travamento (não chegou ao corpo em 6 s; item inalcançável em 12 s). Itens no piche/água funda são
  descartados.
- **Pega como o jogador:** `ItemDrop.RequestOwn()` quando não é dono do item e `Humanoid.Pickup(...)`
  — inclusive com `autoequip` desligado, para ele não trocar de arma ao catar um machado.
- Desligue tudo com `Loot/CatarDropsDeCriatura = false`.

> **Para o companheiro *caçar* javali/cervo** é preciso `Combate/AtacarAnimaisNeutros = true`: fora
> do combate ele ignora a fauna passiva (item #14). Criaturas **hostis** (Greydwarf, esqueleto,
> Neck) ele já ataca nos modos Defensive/Aggressive — e o loot funciona igual.

### Comida, resistência e habilidades (Fase 2.13)

O companheiro deixou de ser decorativo: ele **come**, **cansa** e **evolui habilidades** — como um
membro da equipe.

#### Comida — ele come sozinho

Com comida no **próprio inventário**, ele se alimenta sem você mandar:

```
[ModIA] Comida: o companheiro comeu 'CookedMeat' (slot 0) — +40 vida, +30 stamina, regen 2/10s,
       queima 1500s (comidas ativas: 1/3).
[ModIA] Comida: acabou o efeito de 'CookedMeat' (slot 0); o companheiro vai comer de novo.
```

> Os números vêm do próprio item (`m_food`, `m_foodStamina`, `m_foodRegen`, `m_foodBurnTime`) — a
> linha do log é só um exemplo.

- **Bônus iguais aos do jogador:** vida máxima, stamina máxima e **regeneração de vida** (a cada
  10 s), com a **curva de queima** do jogo (`base × pow(restante/total, 0.3)`: comida quase acabando
  entrega quase nada) e o **efeito de status** da comida (sopas, tortas, hidromel-base…). A
  regeneração passa pelo multiplicador dos efeitos de status — **Molhado/Congelado** atrapalham o
  companheiro como atrapalham você.
- **3 comidas ao mesmo tempo** (o mesmo limite do jogador) e ele mostra o item na mão com a animação
  de comer.
- **Sem desperdício:** ele come quando tem um slot livre, ou — com os 3 cheios — **só** se a
  **vida** (`Comida/LimiarHP`, 50%) ou a **stamina** (`Comida/LimiarStamina`, 30%) estiverem abaixo
  do limiar **e** alguma comida já tiver passado de metade da queima (antes disso o próprio jogo
  recusaria a recarga). Na escolha, ele prefere a comida que cobre a necessidade e, sem necessidade
  nenhuma, a **mais barata** — o banquete bom fica para a hora do aperto.
- Desligue com `Comida/ComerAutomaticamente = false` (ele volta a não ter fome).

> **Onde ele guarda a comida?** No inventário dele — as mesmas frutas, carnes e sopas que ele coleta
> ou que você coloca nele (`modia_equip`/baú). Ele **não** mexe no seu inventário.

#### Resistência (stamina) — correr, atacar, bloquear e minerar gastam

```
[ModIA] Resistencia: o companheiro ficou SEM stamina (ele para de correr e recupera em alguns segundos).
[ModIA] Coleta: sem resistência para golpear; o companheiro está recuperando e volta a bater.
```

- **Correr e nadar** gastam por segundo (`Resistencia/GastoCorrida`, `GastoNado`); **atacar**,
  **bloquear/parry** e **pular** gastam pelo próprio motor do jogo (o custo do ataque já vem
  **reduzido pela perícia da arma**, porque ele usa as nossas habilidades).
- Sem stamina ele **anda em vez de correr** e **não consegue atacar/bloquear** até recuperar (é o
  mesmo comportamento do jogador) — mas **nunca fica preso**: a recuperação é automática depois de
  `Resistencia/AtrasoRegen` (1 s) e a taxa é `Resistencia/TaxaRegen` (6/s, mais rápida quanto mais
  vazio, e multiplicada pelos efeitos de status).
- **Na coleta**, golpear árvore/pedra também custa stamina; quando falta, ele **espera recuperar**
  (não veta o nó por "sem progresso").
- Teto = `Resistencia/StaminaBase` (100, contra os 75 do jogador — margem para ele acompanhar você
  sem ficar para trás) **+ a stamina da comida ativa**.
- `Resistencia/StaminaLigada = false` volta ao comportamento antigo (stamina sempre cheia, nada é
  bloqueado).

#### Habilidades — evoluem com o uso

```
[ModIA] Skill: 'Companheiro' subiu WoodCutting para 12.
[ModIA] Skill: 'Companheiro' subiu Pickaxes para 7.
```

- Ele ganha perícia **fazendo**: derrubar árvore sobe **WoodCutting**, picaretar sobe **Pickaxes**,
  a arma equipada sobe a perícia dela (espada/machado/clava…), bloquear sobe **Blocking** e correr
  sobe **Run**. O nível é o **mesmo tipo de progressão** do jogo (`m_increseStep × fator ×
  Game.m_skillGainRate`; o próximo nível exige `pow(nível+1, 1.5) × 0.5 + 0.5`).
- O nível **vale de verdade**: **menos custo de stamina** no ataque (como no jogador: a perícia da
  arma corta até 33% do gasto), **bloqueio mais forte** (o poder do escudo usa a perícia de
  `Blocking`) e melhores contas de ferramenta.
  > O **dano** do golpe fica como estava de propósito: o multiplicador de dano do jogo
  > (`GetRandomSkillFactor`) usa a curva do jogador, que faria um companheiro de perícia **0** bater
  > com ~40% do dano — ou seja, ficaria **mais fraco que antes** desta fase até treinar muito. Não
  > valia a pena a regressão (ver `CompanionSkillPatches`).
- **Aviso de subida de nível** no HUD (canto superior esquerdo), com o nome e o ícone da perícia
  (`Skills/AvisarSubidaDeNivel`).
- **Ao morrer** ele perde 25% de cada perícia (como o jogador; `Skills/PerderSkillsAoMorrer` /
  `Skills/FatorPerdaAoMorrer`) — e a perda entra **antes** do snapshot, então ele volta com o que
  sobrou.
- `Skills/EvoluirSkills = false` desliga tudo (níveis ficam em 0).

> **O aviso chato não voltou.** Aquele `"… is tamed but missing tameable or monster AI script!"`
> aparecia porque o companheiro é domado e não tem `Tameable` (removido de propósito do prefab) — e
> o patch que o silenciava (v0.3.4) também **matava a evolução de perícia**. Agora o mesmo patch
> **entrega o ganho** para as habilidades do companheiro e **depois** pula o corpo do jogo, então as
> duas coisas ficaram certas ao mesmo tempo (ver `CompanionSkillPatches`).

#### Ele sobrevive ao dismiss/invocar

Comida ativa (com o tempo que ainda faltava queimar), resistência e perícias entram no **snapshot**
(Fase 2.7): **H** e depois **F1** devolve o companheiro **alimentado, com a stamina e as habilidades
que ele tinha** — e isso vale até entre sessões, se `Companheiro/PersistirEntreSessoes` estiver
ligado.

---

### Peso, depósito em baú e tool keeper (Fase 2.14)

Até a 0.4.1 a coleta **encerrava** quando o inventário enchia
(`não consegui pegar o item (inventário cheio?)`). Agora ele tem **peso**, **leva a carga ao baú** e
**volta a trabalhar** — e o *tool keeper* cuida da ferramenta certa.

#### Peso — ele não carrega o mundo

- Teto configurável em **`Companheiro/PesoMaximo`** (padrão **300 kg**; **`0` = sem limite**). O
  jogo não impõe limite de peso a personagens: isso é uma escolha de balanceamento do mod.
- Antes de **catar/coletar** ele soma `ItemData.GetWeight()` ao que já carrega
  (`Inventory.GetTotalWeight()`) e **nunca** passa do teto — vale para os **drops do nó**, os itens
  no chão e o **loot de criatura**. A checagem roda a cada **0,5 s** (nunca por frame).
- **Colhíveis** (framboesa, cogumelo…) também respeitam: a planta sempre solta os itens no chão, então
  colher cheio só desperdiçaria o arbusto.

#### Depósito — ele vai ao baú e volta

Ao estourar o peso (ou ao não caber mais nada), o companheiro:

1. procura o **baú utilizável mais próximo** (`Coleta/RaioBuscaBau`, padrão **30 m**) — tem de ser um
   `Container` de construção (nunca carroça/navio/lápide/"mochila" de personagem), com `ZNetView`
   válida, **não** estar em uso (ninguém com ele aberto) e passar no `PrivateArea.CheckAccess`
   (**a mesma regra das portas**) e, sendo privado, ter sido construído por **você**;
2. **anda até ele**, **deposita** os materiais e **volta** — a **tarefa de coleta continua viva** (o
   recurso e a quantidade são os mesmos) e ele retoma sozinho. Enquanto ele está no baú o relógio do
   nó é renovado: ir ao baú é **trabalho**, não travamento;
3. **sem baú no raio**, ele **PAUSA** (volta a te seguir) e avisa no HUD
   (`Companheiro cheio e sem baú por perto: coleta pausada`) — a tarefa **não** é perdida. Assim que
   houver espaço (você larga/comer algo) ou aparecer um baú, ele **retoma sozinho**.

**O que ele guarda:** materiais, troféus, miudezas e peixes **não equipados** e que **não são
comida**. Equipamento, ferramenta, arma, munição, comida e mead ficam com ele — é o que o mantém
funcionando.

**Limites:** 60 s por viagem e 10 s sem reduzir a distância ao baú (parede, água, barranco) — nunca
fica vagando.

#### Tool keeper — a ferramenta certa, do baú se preciso

- Quando **falta** uma ferramenta (machado/picareta) e **criar** não resolveu (lista sem ferramenta
  capaz ou `Coleta/FornecerFerramentas = false`), ele **procura num baú próximo** a melhor ferramenta
  que atenda e **vai buscá-la**:
  - pela **falta**: só procura o tipo que **realmente falta** (não empresta um machado para quem já
    tem machado e precisa de picareta);
  - pelo **tier do nó** (prata/obsidiana): exige `m_toolTier` ≥ mínimo do nó **e** dano > 0 contra as
    resistências dele, com pontuação por tier → qualidade → dano → durabilidade (ferramenta
    **quebrada** é descartada).
- **`Coleta/GuardarFerramentaNaoUsada`** (padrão `true`) liga o tool keeper: com ele **ligado**, o
  companheiro **procura/empresta** a ferramenta num baú e **devolve** a emprestada (ao baú de origem
  ou o mais próximo com espaço) quando a coleta termina. Ferramenta **criada** pelo mod **nunca** é
  devolvida — senão o mod a criaria de novo, e de novo. Desligado, ele nunca mexe em baú por
  ferramenta.
- **Nada muda por padrão:** com `FornecerFerramentas = true` ele continua **criando** a ferramenta como
  antes; o baú é o **plano B**.

#### Comandos e leitura

| Comando | O que faz |
|---|---|
| `modia_deposit` | Manda o companheiro levar a carga ao baú **agora** (o mesmo caminho de quando ele enche) — atalho de teste |
| `modia_stats` | Mostra `Carga: x/y kg (limite …)`, slots livres, estado do depósito e ferramentas emprestadas |

O menu (**Y**) mostra `Carga: 123/300 kg` no painel **Coleta** quando existe teto configurado.

### Trabalhos (jobs) — Fase 2.16

O companheiro não é mais só "segue e coleta": você **aponta para o trabalho** e ele **executa de
ponta a ponta** (o "aponte-os para o seu trabalho" do [`docs/vision.md`](docs/vision.md)).

```
modia_job hunt 5     # caçar: abate 5 animais por perto e cata o que caiu
modia_job fish       # pescar: vara na mão, arremessa e puxa (sem limite)
modia_farm set 10    # define a fazenda: raio de 10 m no ponto onde você está
modia_job farm       # cultivar: planta, colhe e guarda a colheita
modia_job cook       # cozinhar/bebidas: abastece, espera e retira o que ficou pronto
modia_job smelt      # fundições: abastece forno/alto-forno/carvoeira e retira o produto
modia_job stop       # para o trabalho (o mesmo que modia_stop)
```

#### Como funciona a fila de trabalho

- **Um trabalho por vez.** Quem manda no companheiro é o controlador ativo da fila
  (`CompanionJobQueue`); um comando novo **substitui** o trabalho em andamento (ele é encerrado com
  o motivo "novo trabalho pedido") e, quando o trabalho **acaba**, o companheiro **volta a seguir
  você** sozinho (com o resumo no HUD e no log).
- **Prioridade explícita**, na ordem das camadas do `CompanionAI`:
  **combate > coleta > depósito > loot > TRABALHO > follow**.
  - **Combate:** o alvo hostil é do motor nativo — e o *caçar* vive dele (é o trabalho que entrega a
    presa). Um inimigo de verdade (quem te agride, no modo Defensivo) vence a caça.
  - **Coleta / depósito:** um `modia_gather` ou uma viagem ao baú **pausa** o trabalho (ele espera
    parado, sem contar no relógio do job) e **retoma sozinho** quando a coleta/viagem termina.
  - **Loot:** o caçador espera **catar o abate** antes de sair caçando de novo; o pescador guarda a
    vara enquanto o companheiro vai ao corpo.
- **Nada roda por frame:** a varredura de alvo tem intervalo próprio (`Jobs/IntervaloVarredura`,
  padrão 2 s), usa buffers reaproveitados e o log de eventos é **deduplicado** (a mesma linha não
  repete dentro de 10 s).
- **Rede de segurança:** `Jobs/TempoMaximoPorJob` (padrão 20 min) encerra um trabalho que trava.
- **Dono do movimento:** cada trabalho tem o seu (`MovementOwner.Hunt/Fish/Farm/Cook/Smelt`) — um só
  dirige o passo por frame, como na coleta.

#### Caçar

1. A cada varredura ele procura a **fauna** no raio (`Jobs/RaioHunt`, padrão 40 m): javali, cervo,
   lebre, neck, lox… — **animais que deixam algo cair** (o catálogo é lido dos prefabs do jogo, então
   criaturas de mods entram sozinhas). **Nunca**: jogadores, animais **domados**, chefes, o próprio
   companheiro ou quem o jogo mandou ignorar.
2. A presa é entregue ao **combate que já existe** (o mesmo caminho do modo Aggressive): quem
   persegue, mira, ataca, bloqueia e gasta stamina é o motor do jogo — nenhum ataque foi
   reimplementado.
3. Abateu? Ele **conta o abate** e **cata o que caiu** (o `CompanionLoot` da Fase 2.12, que já espera
   os itens saírem do corpo) — e só depois sai atrás da próxima presa.
4. Repete até a **quantidade** pedida ou até **acabar a caça no raio** (3 varreduras vazias
   seguidas). Uma presa que escapa é vetada por 60 s; uma presa longe demais do jogador (a mesma
   "coleira" do combate) é ignorada — não é fuga, é caça.

> **Pássaros** (`RandomFlyingBird`) ficaram para a **2.16b**: eles não são `Character` e exigem um
> estado próprio de aproximação/arco (o "Birding" da referência).

#### Pescar

1. Ele procura **peixes** na água no raio (`Jobs/RaioFish`, padrão 30 m) e uma **margem** de onde o
   peixe fique ao alcance do arremesso e à vista (linha livre, espaço para ficar de pé).
2. Anda até a margem, **equipa a vara e a isca** do inventário e escolhe a **isca que aquele peixe
   aceita melhor** (`Fish.m_baits`, pela chance). Falta vara/isca? Com
   `Jobs/FornecerVara`/`FornecerIsca` (padrão ligados) o mod cria (FishingRod/FishingBait); sem isso,
   ele avisa que falta.
3. **Descansa** até ter stamina de sobra (`Jobs/StaminaMinimaArremesso`, padrão 60% — puxar peixe
   cansa e um peixe fisgado com a barra vazia se perde) e **arremessa**: a força é **aprendida** de
   onde o flutuador caiu, então ele não joga a linha longe demais.
4. **Espera a fisgada** (até `Jobs/EsperaFisgada`, padrão 60 s), **puxa** como um jogador (larga
   quando o peixe foge ou a linha tensiona, puxa quando ele cansa), **guarda o peixe** e repete até a
   quantidade ou até não haver mais peixe no raio.

**Por que precisou de patch:** o flutuador do jogo (`FishingFloat`) **só conhece jogadores** — ele
guarda o *user id* de quem arremessou e procura o dono na lista de jogadores. Para o companheiro ele
não achava a vara e **se destruía na hora** (e, pior, podia derrubar o **seu** flutuador). Os patches
de `CompanionFishPatches` (`Setup`/`GetOwner`/`ReturnBait`) dão ao flutuador do companheiro uma
**chave própria no ZDO**, resolvem o dono por ela e devolvem a **isca** a ele quando a linha volta
sem peixe (o vanilla só devolve para jogadores).

> **Fora do núcleo (2.16b):** o mapeamento de **todos** os pontos de pesca da região (grade de raios
> em volta do local) e o "voltar para casa com a carga cheia" — hoje a margem é procurada **em volta
> do peixe escolhido** e a carga cheia continua sendo resolvida pelo depósito em baú da 2.14.

#### Cultivar

1. Primeiro você **define a zona de fazenda**: fique no canteiro e use `modia_farm set` (raio padrão
   `Jobs/FarmRaio`, 8 m) ou `modia_farm set 12`. A zona (centro + raio) fica **gravada no `.cfg`**
   (`Jobs/FarmZona`), então sobrevive a reinícios; `modia_farm clear` apaga e `modia_farm info` mostra
   a zona e **quais culturas ele sabe plantar**.
2. Em cada varredura ele olha a zona e, **na ordem**: **colhe** a planta pronta mais perto → **planta**
   sementes do inventário nas vagas livres → se não há nada a fazer mas tem planta crescendo, **espera
   no canteiro** (é o "cuidar do canteiro" até a colheita ficar pronta). Sem semente, sem planta
   crescendo e sem fruta pronta, o trabalho encerra sozinho.
3. **Colher:** a planta pronta é um `Pickable` do jogo e ele usa o `Interact` (o mesmo caminho do
   jogador, só que com o `Humanoid` do companheiro) — o jogo larga os frutos no chão e ele **cata**.
4. **Plantar:** ele consome **uma semente do inventário** e instancia o prefab da planta no ponto,
   com a mesma sequência do jogo (`TerrainModifier.SetTriggerOnPlaced` + `Instantiate` + efeito de
   colocação). A vaga tem de ser **terreno cultivado** (arado com o Cultivator), no **bioma** da
   cultura, **sem planta/peça/colhível por perto** (espaçamento = o maior entre
   `Jobs/FarmEspacamento` e o raio de crescimento da cultura) e **com acesso** (respeita área privada).
5. **Guardar:** a cada `Jobs/FarmDepositarCada` itens colhidos (padrão 20) ele leva a colheita ao
   baú mais próximo — é o **depósito da Fase 2.14**, com uma diferença: as **sementes** (e os
   vegetais que produzem semente) ficam no inventário até `Jobs/FarmManterSementes` (padrão 20), senão
   a primeira viagem ao baú levaria justamente o que ele precisa para replantar.

**O que ele sabe plantar/colher** vem dos **prefabs do jogo** (`FarmCatalog`): o prefab plantável é o
que tem `Plant` + `Piece`, e o custo da peça (`m_resources[0]`) é a **semente** — é assim que se
descobre que `CarrotSeeds` planta a cenoura e que a própria `Carrot` planta o `sapling_carrot_seed`
(a rotação semente ↔ vegetal fecha). Só entram culturas de **terreno cultivado**, o que deixa **mudas
de árvore** de fora.

> **Fora do núcleo:** a **UI** de escolher a cultura e de posicionar a zona com o mouse (o "picker" e
> o "placer" da referência) — hoje a zona vem do comando e a cultura é a que estiver no inventário
> (a config `Jobs/FarmZona` já guarda um campo de cultura para quando a UI chegar); a **rotação de
> culturas em fileiras**; e **buscar/guardar sementes em baús** (ele usa só o que tem no inventário).

#### Cozinhar / preparar bebidas

1. A cada varredura ele procura estações num raio (`Jobs/CozinhaRaio`, padrão 20 m): **espeto /
   forno de cozinha** (`CookingStation`), **barril de fermentação** (`Fermenter`) e **caldeirão /
   mesa de preparo** (`CraftingStation` que exige fogo).
2. **Prioridade das tarefas:** retirar o que **ficou pronto (ou está queimando)** → tornear o
   **barril pronto** → cozinhar no **caldeirão** → **reabastecer o fogo** → **pôr comida** no
   espeto/forno → **encher o barril** vazio. Assim ele nunca deixa a comida queimar no pé nem o fogo
   apagar com o espeto cheio.
3. **Abastecer:** combustível e comida saem do **inventário do companheiro**. O fogo é mantido em
   `Jobs/CozinhaNivelCombustivel` (padrão 50%) do máximo — não precisa encher até o teto, só o
   suficiente para cozinhar. O espeto que **exige fogueira** também é atendido: ele reabastece a
   fogueira embaixo dele.
4. **Esperar:** com tudo abastecido ele fica por perto ("esperando o preparo") e volta a agir assim
   que algo fica pronto — até `Jobs/CozinhaEsperaPreparo` (padrão 4 min), a rede de segurança para um
   espeto sem fogo.
5. **Retirar:** o prato pronto é tirado da estação e **catado** do chão. O barril é **torneirado** e
   as garrafas (que o jogo larga 1,5 s depois) são catadas também.

**Como ele fala com as estações:** pelo **RPC do ZNetView** — o mesmo caminho que o jogo usa por
baixo do `Interact` (`RPC_AddItem`, `RPC_AddFuel`, `RPC_RemoveDoneItem`, `RPC_Tap`). O `Interact` do
jogo mexe em perícia/estatística do **jogador** e na GUI de craft, o que não é do companheiro. O
estado é lido direto do ZDO (`slotN`/`slotstatusN` no espeto, `Content`/`StartTime` no barril).

> **Fora do núcleo:** a **lista curada de receitas** da referência (foco por chefe, prioridades,
> "quanto já tem na mochila") — aqui o caldeirão escolhe a primeira receita habilitada da estação
> para a qual ele tem todos os ingredientes; **colmeias** e a manutenção da base (Fase 2.17); e
> **buscar combustível/comida em baús** (os insumos vêm do inventário). O caldeirão entrega o prato
> direto no inventário (o jogo só tem o caminho da GUI de craft, que é do jogador) — sem o bônus de
> rendimento por perícia.

#### Fundições

1. Ele procura **fornos** num raio (`Jobs/FundicaoRaio`, padrão 20 m): **forno** e **alto-forno**
   (`Smelter`) e **carvoeira** (`Smelter` com `m_maxFuel == 0`, que faz carvão de madeira sem
   combustível).
2. **Prioridade:** **catar o produto** (barra/carvão) que a fornalha largou no chão → **combustível**
   → **minério**. O minério é escolhido pelas **conversões do próprio forno** (`m_conversion`), então
   o mesmo código serve para forno, alto-forno e carvoeira.
3. **Abastecer:** ele põe uma unidade por vez (como o jogador) via `RPC_AddOre`/`RPC_AddFuel`, até
   `Jobs/FundicaoPorViagem` por ida (padrão 30) e conferindo o ZDO antes de cada unidade (fila cheia
   ou combustível no teto: ele para).
4. **Retirar:** o `Smelter` larga o produto no **ponto de saída** sozinho (quando a fila de minério
   esvazia ou o combustível acaba) — o companheiro **cata** o que apareceu e guarda no inventário
   (e no baú, pelo depósito da 2.14, quando a carga aperta).

> **Fora do núcleo:** **buscar minério/combustível em baús** (a referência usa o tool keeper e as
> pilhas de recurso — aqui, como pede a tarefa, os insumos vêm do inventário); a **ordenação entre
> vários fornos** por fila/percentual; e **pilhas de recurso no chão**.

#### Leitura e HUD

| Onde | O que mostra |
|---|---|
| HUD (canto superior esquerdo) | início do trabalho, cada abate/peixe/colheita/produto e o encerramento (com o motivo) |
| `modia_stats` (F5) | `Trabalho: Cultivar (3, 2.1 min) — colhendo Carrot` (ou `nenhum`) |
| `modia_job` sem argumento | o trabalho atual |
| `modia_farm` sem argumento | a zona de fazenda e as culturas plantáveis |
| log do BepInEx | cada passo relevante, deduplicado |

### Base e casa (Fase 2.17)

O companheiro ganha um **lar** — e passa a **cuidar dele**. São duas coisas: a **casa** (onde é a
base) e o **trabalho de cuidado** (o que ele faz lá).

#### A casa — onde é a base

Fique no lugar que você chama de casa e use `modia_home set` (raio padrão `Casa/HomeRaio`, 20 m) ou
`modia_home set 25`. A casa (centro + raio) fica **gravada no `.cfg`** (`Casa/HomeZona`), então
sobrevive a reinícios; `modia_home clear` apaga e `modia_home info` mostra o estado.

```
modia_home set 25    # define a casa no ponto onde você está, com 25 m de raio
modia_home info      # mostra a casa, o cuidado da base e o que ele faz dentro dela
modia_home clear     # apaga a casa
```

A casa é a referência de três coisas:

1. **Depósito (`Casa/PreferirBauDaCasa`, ligado por padrão):** ao encher, ele prefere um **baú dentro
   da casa** — um baú da casa leva um desconto de distância igual ao raio dela na escolha do depósito
   (2.14), então ele atravessa a base para depositar "em casa" em vez de usar um baú qualquer do lado
   de fora. Sem casa definida, vale só a distância (como antes).
2. **Cuidado da base:** o trabalho de manutenção trabalha **dentro do raio** da casa.
3. **Voltar quando perdido (`Casa/VoltarSePerdido`, DESLIGADO por padrão):** se você atravessou um
   portal, morreu longe ou ele ficou preso no terreno, ele **volta para a base** (e espera por você)
   em vez de atravessar o mundo atrás de você. Só age quando ele está a mais de
   `Casa/DistanciaPerdido` (80 m) de você **e** longe da casa, e sem nada mais no comando.

#### Cuidar da base — o trabalho `upkeep`

```
modia_job upkeep     # uma passada: cuida do que estiver precisando e encerra
modia_job upkeep 10  # cuida de 10 coisas (reparos, abastecimentos, colmeias, arrumações)
modia_basecare on    # modo contínuo: ele cuida da casa sozinho quando fica ocioso
modia_basecare off   # desliga o modo contínuo
modia_basecare       # mostra o estado
```

Dentro do raio da casa ele, **em ordem de prioridade**:

1. **REPARA** as construções danificadas. Ele pega o **martelo de construção**, anda até a peça e dá
   o golpe de reparo **pelo caminho do jogo** (`WearNTear.Repair` → `RPC_Repair`, o mesmo do
   `Player.Repair`): vale a **estação de craft da peça** no alcance (bancada, forja…), o acesso em
   área privada e a chave de mundo `NoWorkbench`. **Não gasta material** — o custo é **stamina** (a
   conta do jogo, reduzida pela perícia) e a **durabilidade do martelo**. Ele só repara abaixo de
   `Base/RepararAbaixo` (90% por padrão: o desgaste leve de chuva passa) e, se não tiver martelo
   nenhum, o mod cria um (`Base/FornecerMartelo`). Peças sem a bancada por perto ficam de fora —
   como ficariam para um jogador.
2. **ABASTECE** fogueiras, tochas e braseiros (`Fireplace`) com o **combustível do inventário**
   (madeira, carvão, resina…), uma unidade por vez como o jogador, mantendo
   `Base/FogueiraNivel` (50%) do combustível máximo. Sem combustível na mochila ele não busca em
   baús nesta fase.
3. **ESVAZIA AS COLMEIAS** com mel (`Beehive`): usa o `RPC_Extract` do jogo (o mel cai no chão) e
   **cata o mel**. Só mexe em colmeias com pelo menos `Base/ColmeiaMinima` (50%) do mel máximo.
4. **ORDENA OS BAÚS** — "igual com igual": para cada item repetido em mais de um baú, ele leva o que
   está no baú com **menos** para o baú que **já tem mais** daquele item, carregando as pilhas na mão
   (`Base/OrdenarPorViagem` por viagem) e voltando. Depois **junta as pilhas parciais** do mesmo item
   dentro do mesmo baú (`Base/JuntarPilhas`). O que pode ser movido sai de `Base/OrdenarItens`
   (`*` = todos os empilháveis; equipamento/ferramenta só se estiverem na lista). Ele mexe nos baús
   **direto** (nada de abrir a GUI) e confere o espaço antes de cada movimento — **nunca perde item**.

- **Modo contínuo (`modia_basecare on` ou `Base/Continuo`):** com ele ligado, o companheiro **retoma
  o cuidado da casa sozinho** sempre que fica ocioso (sem trabalho, coleta, depósito, loot ou
  combate) e, quando não há nada para cuidar, **volta ao centro da base e espera** em vez de encerrar.
  Precisa de uma casa definida. `modia_stop`/`modia_job stop` **desliga** o modo contínuo (senão ele
  voltaria a cuidar da casa segundos depois).
- **Varredura:** a cada `Base/IntervaloVarredura` (3 s), com buffers reaproveitados — nada por frame.
  Um alvo que não deu para alcançar (parede, água) sai da fila por um tempo em vez de ser tentado para
  sempre.

> **Fora do núcleo (ficou de fora desta fase):** o **visual da zona da casa** (o anel no chão que a
> referência desenha enquanto você ajusta) e o **painel de ajuste da casa** (`HomeZonePanel`) — aqui a
> casa é definida por comando e confirmada pelo `modia_home info`; as **pilhas de recurso no chão**
> (`UpkeepPiles`); **buscar combustível/insumos em baús**; e a lista de itens por **categoria**.
> O **descanso/dormir na casa** chegou na Fase 2.18 (abaixo).

#### Leitura e HUD

| Onde | O que mostra |
|---|---|
| `modia_stats` (F5) | `Casa: 12,4 (raio 20 m), bau da casa preferido no deposito.` e `Cuidado da base: ligado = True, so por comando, tarefas: reparar, abastecer, colmeias, ordenar baus.` |
| `modia_home` / `modia_basecare` sem argumento | a casa + o cuidado da base (e, sem casa, o lembrete do `modia_home set`) |
| `modia_job` sem argumento | o trabalho atual (ex.: `Cuidar da base (3, 1.2 min) — reparando Parede de madeira`) |
| HUD | início, cada reparo/abastecimento/colmeia/arrumação e o encerramento |
| log do BepInEx | cada passo relevante, deduplicado |

### Qualidade de vida (Fase 2.18)

Quatro coisas que fazem o companheiro parecer **gente**: ele **descansa no fogo**, **dorme na cama**,
**atravessa o portal com você** e **puxa a sua carroça**. Nada disso atrapalha o resto — cada uma só
age quando ele está **livre** (sem trabalho, coleta, depósito, loot ou combate) e com você por perto.

#### Sentar no fogo e descansar

Quando ele fica **ocioso** e você está a menos de `Descanso/RaioJogador` (12 m), ele **senta** para
descansar se houver uma **fogueira acesa** num raio de `Descanso/RaioFogueira` (10 m) — ou se estiver
**dentro da casa** (`modia_home set`). Sentado ele:

- **recupera resistência mais rápido** (`Descanso/MultiplicadorStamina`, padrão **2×**, e sem o
  atraso de regeneração);
- **recupera vida** (`Descanso/TaxaCura`, padrão 3/s);
- depois de `Descanso/TempoParaDescansar` (20 s) ganha o **DESCANSO** — o mesmo buff do jogo, com a
  **duração vinda do conforto** do lugar (`m_baseTTL + (conforto − 1) × m_TTLPerComfortLevel`, com o
  conforto calculado como para o jogador: cobertura + peças de conforto), que dá regeneração de
  stamina, cura e **bônus de perícia** (`Descanso/BonusSkill`, padrão +0.5, o `m_raiseSkillModifier`
  do `SE_Rested`).

Se **você se afastar** (mais de 1,5× o `RaioJogador`), se aparecer **inimigo** num raio de 15 m, se a
fogueira **apagar**, se ele **sair da casa** ou se ele **voltar a ter trabalho**, ele **levanta** e
volta a te seguir na hora. Para mandar na mão: `modia_rest sit` (procura fogueira num raio maior e,
sem fogueira, senta onde está se estiver na casa) e `modia_rest stop` para levantar.

#### Dormir na cama

Ele **dorme numa cama livre** (`Bed`) quando **anoitece** (`Descanso/DormirDeNoite`) ou quando
**você se deita** (`Descanso/DormirQuandoVoceDorme`) — desde que haja uma cama num raio de
`Descanso/RaioCama` (25 m). Dormindo, ele **fica na cama** (com a animação de deitar e o campo
`inBed` no ZDO — é o que faz a **noite passar mais rápido** quando todos estão dormindo, como no
jogo) e **acorda** quando **amanhece**, quando **você levanta**, se aparecer **inimigo**, se a cama
sumir ou se você se afastar mais de `Descanso/DistanciaAcordar` (30 m). Ao levantar, ele sai **ao
lado** da cama e fica com o **descanso** aplicado.

> **A sua cama é sua.** Ele **nunca** usa a cama que é o **seu ponto de spawn atual** nem a cama de
> outro jogador: para ele dormir, construa uma **segunda cama** (uma cama sem dono também serve).
> Sem cama livre, ele apenas senta no fogo.

```
modia_rest            # estado: ocioso/descansando/dormindo, e o descanso que falta
modia_rest sit        # manda sentar e descansar agora (fogueira acesa mais próxima, ou na casa)
modia_rest sleep      # manda dormir agora na cama LIVRE mais próxima (mesmo de dia)
modia_rest stop       # levanta e volta a seguir você
```

#### Seguir você pelo portal

No Valheim quem atravessa um portal é o **jogador** — um NPC não tem como entrar nele. Então o
companheiro faz o que um jogador faria **a pé**: quando você atravessa um portal, ele **anda até o
portal** e, quando o seu teleporte **termina**, é levado para **junto de você** (do outro lado), em
vez de ficar para trás. Funciona com **qualquer** teleporte seu (portal, morte, comando de console) e
não entra em loop: é **uma viagem por teleporte**, e ele **não** é levado quando já está perto (o
mesmo lado da base).

Ele **não** segue se estiver **ocupado** — com um trabalho, coleta, carga para depositar, loot,
alvo de combate ou puxando a carroça. Nesse caso ele fica onde está; se quiser que ele volte sozinho,
ligue o `Casa/VoltarSePerdido` da Fase 2.17. Config: `Portal/Ligado`, `Portal/RaioPortal`,
`Portal/AlcancePortal`, `Portal/TempoMaximo`.

#### Puxar a sua carroça

```
modia_cart pull       # ele anda até o engate da carroça (Vagon) livre mais próxima e puxa
modia_cart release    # solta a carroça
modia_cart            # estado: nenhuma / indo puxar / puxando
```

Ele **anda até o engate** da carroça (`Vagon`) e se **prende nela pelo caminho do jogo**
(`Vagon.Interact` → o `ConfigurableJoint` que o próprio Valheim cria quando **você** puxa), então a
carroça vem **atrás dele** enquanto ele te segue — subindo a ladeira que você subiu. Se a carroça
ficar presa demais a junta arrebenta e ele solta sozinho (e volta a te seguir). Ele **nunca engata
sozinho** e a **carroça não atravessa portal** com você.

> **Fora do núcleo (ficou de fora desta fase):** o **menu radial** da referência
> (`CompanionRadialMenu`) — aqui o pedido é por comando (`modia_rest`, `modia_cart`); o **engate
> automático** da carroça e o **transporte dela por portal**; o `Vagon` **empurrado** (só puxa, pelo
> joint do jogo); o **"stay awake"** e a **cama reivindicada no ZDO da cama** (aqui a cama é escolhida
> a cada descanso); o **roster/shrine/respawn** da referência (`MHR_Roster`, `MHR_Shrine`,
> `MHR_RespawnTracker`, `CompanionTombstoneResurrect`); os **saltos** (`MHR_Jump`); a **segurança na
> água** (`WaterSafety`); a **tocha** (`TorchOpener`); e o **seguir por teleporte quando ele fica
> preso** (o `FollowStuckTeleport` — aqui só o portal do jogador puxa o companheiro).

#### Leitura e HUD

| Onde | O que mostra |
|---|---|
| `modia_stats` (F5) | `Descanso: sentado descansando, descansado (245s).`, `Portal: ligado (ele atravessa com voce).` e `Carroca: puxando a carroca.` |
| `modia_rest` sem argumento | o estado do descanso/sono + o do portal |
| `modia_cart` sem argumento | o estado da carroça |
| HUD | `Companheiro descansando` / `dormindo` / `descansado (…s)` e `atravessou o portal com voce` / `puxando a carroca` |
| log do BepInEx | cada decisão relevante (sentou, levantou, dormiu, acordou, atravessou, engatou, soltou) |

### Configuração (seções do `.cfg`)

- **Geral** — `LogDetalhado`
- **Teclas** — `SpawnKey` (F1), `DismissKey` (H), `CombatModeKey` (F3), `HarvestKey` (F4), `MenuKey` (Y)
- **Companheiro** — `EquiparLoadoutNoSpawn`, `WeaponPrefab`, `ArmorSetPrefabs`, `PersistirEntreSessoes`,
  `PesoMaximo`
- **Aparencia** — `AparenciaPadrao`, `CabeloPrefab`, `BarbaPrefab`, `CorPele`, `CorCabelo`
- **Combate** — `Modo`, `RaioAgressivo`, `RaioDefensivo`, `Coleira`, `AtacarAnimaisNeutros`, `IntervaloVarredura`,
  `BloquearAtaques`, `RaioBloqueio`, `TempoBloqueio`
- **Movimento** — `ProtecaoContraQueda`, `QuedaMaxima`, `DistanciaSondagem`, `AbrirPortas`
- **Coleta** — `RecursoPadrao`, `QuantidadePadrao`, `RaioVarredura`, `IntervaloVarredura`,
  `PausarEmCombate`, `CatarDrops`, `Camadas`, `FornecerFerramentas`, `FerramentasFornecidas`,
  `DepositarAoEncher`, `RaioBuscaBau`, `GuardarFerramentaNaoUsada`
- **Loot** — `CatarDropsDeCriatura`, `RaioCatar`, `DistanciaMaxima`, `LimiteFila`, `TempoEspera`,
  `TempoMaximo`, `PesoMaximo`
- **Comida** — `ComerAutomaticamente`, `LimiarHP`, `LimiarStamina`, `IntervaloVerificacao`
- **Resistencia** — `StaminaLigada`, `StaminaBase`, `GastoCorrida`, `GastoNado`, `TaxaRegen`, `AtrasoRegen`
- **Skills** — `EvoluirSkills`, `AvisarSubidaDeNivel`, `PerderSkillsAoMorrer`, `FatorPerdaAoMorrer`
- **Jobs** — `Ligado`, `QuantidadePadrao`, `TempoMaximoPorJob`, `IntervaloVarredura`, `RaioHunt`,
  `RaioFish`, `DistanciaArremesso`, `EsperaFisgada`, `StaminaMinimaArremesso`, `FornecerVara`,
  `VaraPrefab`, `FornecerIsca`, `IscaPrefab`, `IscaQuantidade`,
  `FarmZona`, `FarmRaio`, `FarmPlantar`, `FarmColher`, `FarmEspacamento`, `FarmDepositarCada`,
  `FarmManterSementes`, `CozinhaRaio`, `CozinhaCombustivel`, `CozinhaNivelCombustivel`,
  `CozinhaRetirarPronto`, `CozinhaCaldeirao`, `CozinhaEsperaPreparo`, `FundicaoRaio`,
  `FundicaoCombustivel`, `FundicaoRetirarProduto`, `FundicaoPorViagem`
- **Casa** — `HomeZona` (escrita pelo `modia_home set`), `HomeRaio`, `PreferirBauDaCasa`,
  `VoltarSePerdido`, `DistanciaPerdido`
- **Base** — `Ligado`, `Continuo`, `RaioBase`, `IntervaloVarredura`, `Reparar`, `RepararAbaixo`,
  `FornecerMartelo`, `MarteloPrefab`, `AbastecerFogueiras`, `FogueiraNivel`, `Colmeias`,
  `ColmeiaMinima`, `OrdenarBaus`, `OrdenarItens`, `OrdenarPorViagem`, `JuntarPilhas`
- **Descanso** — `Ligado`, `SentarDescansar`, `RaioFogueira`, `RaioJogador`, `DormirNaCama`,
  `DormirDeNoite`, `DormirQuandoVoceDorme`, `RaioCama`, `DistanciaAcordar`, `MultiplicadorStamina`,
  `TaxaCura`, `TempoParaDescansar`, `BonusSkill`
- **Portal** — `Ligado`, `RaioPortal`, `AlcancePortal`, `TempoMaximo`
- **Carroca** — `Ligado`, `Raio`
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
│  ├─ Companion/              # CompanionAI, Prefab, Manager, Inventory, Food, Stamina, Skills, Appearance, Combat, Harvest, Loot, Deposit, ToolKeeper, Hover, Menu, Home, BaseCare, Rest, PortalFollow, Cart...
│  │  └─ Jobs/                # trabalhos: CompanionJob, CompanionJobQueue, HuntJob, FishJob, FarmJob (+ FarmZone/FarmCatalog), CookJob, SmeltJob, UpkeepJob (+ BaseCareConfig), JobStations + patches do flutuador
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

### Teste rápido do fim do loop da coleta (Fase 2.11)

1. **Volta a seguir**: invoque o companheiro (**F1**), inicie a coleta (`modia_gather wood 20` ou
   **F4**) numa floresta e, assim que ele começar a bater numa árvore/tora, **saia andando para
   longe** (mais de ~55 m do nó, o raio de varredura + 15 m). Ele deve **largar o nó e voltar a
   seguir você** — e não ficar preso batendo/largando no mesmo lugar.
2. **Log sem repetição**: o log (`BepInEx\LogOutput.log`) deve ter **uma** linha
   `nó '…' largado (jogador se afastou do nó)` (no máximo uma a cada 10 s para o mesmo nó) em vez de
   centenas de linhas iguais; voltando para perto, ele escolhe um nó novo (`alvo '…'`) normalmente.
3. **Diagnóstico**: com `Geral/LogDetalhado = true`, a varredura mostra quantos nós foram descartados
   por estarem `longe do jogador` — é o filtro novo, que é o **mesmo** da guarda de distância.
4. **Sem aviso de domesticação**: o aviso `"<nome> is tamed but missing tameable or monster AI
   script!"` não deve mais aparecer no log ao bater nas árvores (era benigno — ver `CHANGELOG` 0.3.4).
5. **Nada mais mudou**: spawn/dismiss, follow, combate, aparência, hover, menu e persistência seguem
   iguais; a coleta normal (árvore → tora → madeira, pedra, cascata) continua funcionando.

### Teste rápido do loot de criatura (Fase 2.12)

1. **Preparo**: **F1** invoca o companheiro e equipe uma arma de verdade nele
   (`modia_equip AxeStone` ou `modia_equip SwordBronze` — o `Club` padrão também serve).
2. **Fauna passiva (javali/cervo)**: no `.cfg`, ligue `Combate/AtacarAnimaisNeutros = true` (o padrão
   é `false`, e nesse caso ele **ignora** a fauna neutra por design — item #14). No console do jogo
   (**F5**) rode `spawn Boar 2` (ou ache um cervo/javali no mundo) e deixe o companheiro matar.
   - O log deve mostrar `Loot: o companheiro abateu 'Javali' ...` → `Loot: indo catar o que 'Javali'
     deixou ...` → `Loot: +1x 'LeatherScraps'` / `+1x 'BoarTusk'` → `Loot: fim do abate de 'Javali'
     (peguei tudo que caiu)`.
   - `modia_inv` (console) deve listar `LeatherScraps`/`BoarTusk` no inventário do companheiro.
   - **Cervo** (`spawn Deer 1`) rende `DeerHide` e `RawMeat` — é o caso clássico de "couro no chão".
3. **Criatura hostil (sem mexer em config)**: em modo **Aggressive** ou **Defensive** (**F3**),
   deixe-o matar um **Greydwarf** (`spawn Greydwarf 1`) — ele cata `GreydwarfEye`, `Resin`, `Wood`,
   `Stone`. Esqueleto (`spawn Skeleton 1`) rende `BoneFragments`.
4. **Só o que o abate gerou**: antes da briga, jogue alguns itens no chão perto (drop manual com
   `Q`). Depois do abate, o companheiro deve catar **só** os drops da criatura e **deixar** os seus
   itens no lugar.
5. **Prioridade**: comece uma coleta (`modia_gather wood 20`) e mate algo no meio: o log mostra
   `Loot: '<criatura>' de volta à fila (coleta em andamento)` e o companheiro **continua coletando**;
   ao parar a coleta (`modia_stop`), ele **volta** ao corpo e cata.
6. **Limites**: com `Loot/CatarDropsDeCriatura = false` ele não cata nada; com
   `Loot/DistanciaMaxima = 8`, sair andando depois do abate faz o log mostrar
   `fim do abate de '...' (o jogador seguiu em frente)`. Com `Loot/PesoMaximo = 5`, ele para de catar
   ao passar de 5 kg (`carga acima de 5 kg`).

### Teste rápido de comida, resistência e habilidades (Fase 2.13)

1. **Preparo**: **F1** invoca o companheiro. No console do jogo (**F5**), dê comida e ferramentas a
   ele:
   ```
   modia_equip AxeStone
   ```
   Depois jogue comida no chão e mande-o catar (ou use um baú/`modia_gather` de `Raspberry`): ele só
   come o que estiver **no inventário dele**.
   > Dica: `modia_gather raspberry 10` enche o inventário dele de comida rapidinho.
2. **Ele come sozinho**: rode `modia_stats` antes e depois de alguns segundos.
   - O log deve mostrar `Comida: o companheiro comeu '...' (slot 0) — +… vida… (comidas ativas: 1/3)`
     e o `modia_stats` deve mostrar `Comida (1/3): …` com o **bônus de vida** (e a **vida máxima**
     subindo no menu do jogo: **Y** perto dele).
   - Coma você perto dele: ele mantém as 3 comidas ativas, como um jogador.
   - Espera o efeito acabar (ou use `Comida/IntervaloVerificacao = 1` e uma comida curta, tipo
     `Raspberry`): o log mostra `acabou o efeito de '...'` e, na próxima verificação, ele come de
     novo.
3. **Ele cansa**: corra (ou use `Combate/Modo = Aggressive` e provoque um inimigo) e olhe
   `modia_stats` → `Resistencia: x / y (z%)`.
   - Correndo longe, o valor cai; ao zerar aparece
     `Resistencia: o companheiro ficou SEM stamina…` e ele **passa a andar** (para de correr) até
     recuperar. Bloquear/atacar também gastam — o número no menu cai na hora.
   - `modia_gather wood 5` também gasta: com a stamina zerada aparece
     `Coleta: sem resistência para golpear; o companheiro está recuperando e volta a bater` — e ele
     **volta a bater** depois (o nó **não** é vetado).
   - `Resistencia/StaminaLigada = false` → a stamina fica sempre cheia e nada é bloqueado.
4. **As habilidades sobem**: com **F4** (ou `modia_gather wood 10`), deixe-o derrubar uma árvore.
   - O log deve mostrar `Skill: 'Companheiro' subiu WoodCutting para N` e o HUD mostra
     `Companheiro — subiu de nível ... WoodCutting: N`.
   - `modia_stats` lista as perícias (`Pericias: WoodCutting 12, Axes 8…`). Minerando sobe
     **Pickaxes**; lutando sobe a perícia da arma e **Blocking**; correndo sobe **Run**.
   - `Skills/EvoluirSkills = false` → nada sobe (e nenhum aviso).
   - Confirme que o aviso `"… is tamed but missing tameable or monster AI script!"` **não** aparece
     no `LogOutput.log` enquanto ele bate nas árvores.
5. **Persistência**: rode `modia_stats`, depois **H** (dispensar) e **F1** (invocar). O companheiro
   volta com as **mesmas comidas ativas** (com o tempo que faltava), a **stamina** e as
   **perícias** — `modia_stats` confirma. `modia_state` mostra o snapshot com
   `Comida/stamina/perícias (Fase 2.13): …`.
6. **Nada mais mudou**: spawn/dismiss, follow, combate, coleta (madeira/pedra/minério), loot,
   movimento, aparência, hover, menu e persistência seguem iguais — com a diferença de que agora ele
   come, cansa e evolui.

### Teste rápido de peso, depósito em baú e tool keeper (Fase 2.14)

**O teste que o item #7 da auditoria pede: encher o inventário e conferir que ele deposita e retoma.**

1. **Preparo (base com baú):** construa um **baú de madeira** perto de onde você vai coletar (a até
   30 m — `Coleta/RaioBuscaBau`) e deixe-o **vazio**. Para o teste ser rápido, baixe o teto:
   ```
   Companheiro/PesoMaximo = 40      # (ou 0 para "sem limite", o comportamento antigo)
   Coleta/DepositarAoEncher = true
   Coleta/RaioBuscaBau = 30
   ```
   > Dica: quanto **menor** o teto, mais rápido o teste — uma única pilha de madeira já estoura 40 kg.
   > Com o teto padrão (300 kg) são precisos alguns nós, o que também serve (o `modia_gather wood`
   > coleta sem limite de quantidade).
2. **F4** (ou `modia_gather wood`) perto de um baú e de árvores. O log deve mostrar:
   - `Depósito: o companheiro está cheio; levando a carga para 'Baú de madeira' a 12 m (ele volta a coletar depois).`
   - `Depósito: fim da viagem ao baú (guardei 3 item(ns)); 3 item(ns) movido(s).`
   - e o HUD: `Companheiro cheio: levando a carga ao baú (12 m)` → `Carga guardada no baú (3 item(ns))`.
   - **A coleta NÃO encerra:** depois do depósito ele **volta ao trabalho** sozinho (o log volta a
     mostrar `Coleta: alvo '...'` e os `+Nx 'Wood'`). Antes da 0.4.2 aparecia
     `Coleta encerrada (não consegui pegar o item (inventário cheio?))` e a tarefa morria.
   - Confirme no baú: os materiais estão lá (`modia_inv` mostra o inventário dele **sem** a madeira;
     abra o baú no jogo e veja o conteúdo).
3. **Sem baú:** dispense/recolha o baú (ou vá para longe dele) e deixe-o encher de novo. Agora:
   - `Coleta: o companheiro está cheio e não conseguiu depositar (carga acima do limite); a coleta fica PAUSADA (a tarefa não é perdida).`
   - HUD: `Companheiro cheio e sem baú por perto: coleta pausada`; ele **para de coletar** e volta a te
     seguir (não fica batendo na árvore sem pegar nada).
   - Largue algo da carga dele (`modia_drop Wood 20`) ou aumente `PesoMaximo` em jogo (F1 do BepInEx
     reconfigura no próximo boot): o log mostra `Coleta: carga e espaço liberados; retomando a coleta.`
     e ele volta a coletar **sozinho**.
4. **`modia_deposit` (teste manual):** com o companheiro cheio e perto de um baú, rode `modia_deposit`.
   Ele deve sair andando até o baú, depositar e voltar a te seguir
   (`Depósito: o companheiro está cheio; levando a carga para '...' a N m`).
5. **Carga no `modia_stats` e no menu:** rode `modia_stats` → `Carga: 41/40 kg (limite 40 kg) | 12 item(ns), 20 slot(s) livre(s)`
   e `Deposito: nenhuma viagem (raio 30 m) | Tool keeper: nenhuma ferramenta emprestada`. Com o menu
   aberto (**Y**, perto dele) o painel **Coleta** mostra `Carga: 41/40 kg`.
6. **Tool keeper (a ferramenta vem do baú):** para ele **emprestar** em vez de criar:
   ```
   Coleta/FornecerFerramentas = false
   ```
   Ponha um **machado** (`AxeStone`) num baú a até 30 m e rode `modia_gather wood`. Como ele não tem
   machado, o log deve mostrar:
   - `Tool keeper: encontrei 'AxeStone' no baú 'Baú de madeira' a 8 m.`
   - `Tool keeper: indo buscar 'AxeStone' no baú '...' a 8 m.` → `Depósito: fim da viagem ao baú (peguei 'AxeStone')`
   - e ele **volta a coletar** com o machado emprestado.
   - Ao terminar a coleta (**F4** de novo ou `modia_stop`), com `Coleta/GuardarFerramentaNaoUsada = true`:
     `Tool keeper: indo devolver ferramenta emprestada no baú '...'` e a ferramenta volta ao baú.
   - Confira com `modia_stats` → `Tool keeper: 1 emprestada(s): AxeStone` enquanto ela estiver com ele
     e `nenhuma ferramenta emprestada` depois de devolver.
7. **Peso no loot:** com `Loot/CatarDropsDeCriatura = true` e o companheiro no teto de peso, abata um
   javali com ele: o abate volta para a frente da fila
   (`Loot: 'Javali' de volta à fila (carga cheia (levando ao baú))`), ele deposita e **volta a catar** —
   o couro não fica no chão.
8. **Nada mais mudou:** spawn/dismiss, follow, combate, coleta, loot, comida/stamina/skills, movimento,
   aparência, hover, menu e persistência seguem iguais. Com `Companheiro/PesoMaximo = 0` e
   `Coleta/DepositarAoEncher = false` o comportamento é **exatamente** o da 0.4.1.

### Teste rápido dos trabalhos (Fase 2.16)

1. **Caçar:** invoque o companheiro (F1), deixe o modo em **Defensivo** ou **Agressivo**
   (`modia_mode defensive`) e vá a um campo com javalis/cervos.
   - `modia_job hunt 2` → no HUD: `Trabalho: Caçar (2x)`; no log:
     `[ModIA] Caçar: catalogo com N tipo(s) de presa` e `indo atrás de 'Javali' a 12,3 m`.
   - Ele **persegue e ataca** com a arma que tem (o combate de sempre) e, ao abater, sai no log
     `abati 'Javali' (abate 1/2)` + `Abate: Javali (1/2)` no HUD; em seguida ele **cata o couro** (a
     camada de loot da 2.12: `Loot: ...` no log) e só então vai atrás do próximo.
   - Com a quantidade atingida: `[ModIA] Trabalho Caçar encerrado (meta atingida): 2/2 em 1,4 min.` e
     ele volta a te seguir. `modia_stats` mostra `Trabalho: nenhum (use modia_job hunt|fish|farm|cook|smelt)`.
   - **Modo passivo:** `modia_mode passive` + `modia_job hunt` → recusa com aviso ("ele nao ataca
     nada"); trocar para passive **durante** o trabalho encerra a caça.
2. **Caça sem fauna:** num lugar sem animais, `modia_job hunt` → depois de 3 varreduras (~6 s)
   `Trabalho Caçar encerrado (acabou a caça: nenhuma fauna a menos de 40 m)`.
3. **Pescar:** vá a uma margem com peixe por perto (ex.: o mar ou um rio), com o companheiro a até
   ~30 m de água com peixe, e `modia_job fish`.
   - No log: `Pescar: vara fornecida ao companheiro ('FishingRod')` (na primeira vez),
     `indo atras de 'Fish1' a ... m (isca 'FishingBait', margem a ... m)`, `arremessei em 'Fish1'
     (14 m, 58% de forca, isca 'FishingBait')`, `linha fora 14 m (isca 'FishingBait')`.
   - Ele fica na margem com a **vara na mão** esperando a fisgada; quando o peixe morde:
     `fisgou — puxando`, depois `'Fish1' na linha — puxando` e, ao puxar até o fim,
     `peguei 'Fish1' (peixe 1/...)` + `Pesquei: Fish1 (1)` no HUD.
   - Se o peixe fugir/arrebentar a linha, o log diz (`escapou`) e ele **arremessa de novo** — sem
     travar. `modia_stats` mostra o progresso. `modia_job stop` (ou `modia_stop`) para tudo e ele
     **guarda a vara**.
   - **Sem vara e sem `Jobs/FornecerVara`:** `modia_job fish` recusa com "Nao da para pescar: sem vara
     de pesca ('FishingRod')".
4. **Prioridade:** comece um `modia_job hunt` e, no meio, mande `modia_gather wood 20`: o trabalho
   **pausa** (`Trabalho: pausado enquanto a coleta esta no comando.`) e **retoma** quando a coleta
   acabar. `modia_stop` para os dois.
5. **Nada mais mudou:** spawn/dismiss, follow, combate, coleta, loot, comida/stamina/skills, peso/
   depósito/tool keeper, movimento, aparência, hover, menu e persistência seguem iguais — com
   `Jobs/Ligado = false` o `modia_job` recusa e o mod fica exatamente como na 0.4.2.

### Teste rápido de cultivar, cozinhar e fundir (Fase 2.16b)

> **Como dar insumos ao companheiro:** o jeito mais direto é **jogar o item no chão** e mandar
> `modia_gather <prefab> <qtd>` (ele cata itens do chão — é a coleta da Fase 2.4b). Vale para
> sementes, comida, combustível e minério.

1. **Cultivar — zona:** arade o canteiro com o **Cultivator** (o jogo exige terreno cultivado), fique
   no meio dele e use `modia_farm set 8` → no console
   `Zona de fazenda definida em X, Z com raio 8 m (gravada em Jobs/FarmZona)`. `modia_farm` (sem
   argumento) mostra a zona e as culturas que ele sabe plantar (`Culturas plantaveis (N): ...`).
2. **Cultivar — sementes:** jogue `CarrotSeeds` no chão e `modia_gather CarrotSeeds 20` → ele cata e
   volta a te seguir.
3. **Cultivar — plantar:** `modia_job farm` → no log
   `[ModIA] Cultivar: indo plantar 'CarrotSeeds' em X, Z` e `plantei 'CarrotSeeds' em X, Z (19 restantes)`.
   Ele repete até acabar a semente ou o espaço. Sem nada para fazer e com planta crescendo, o status
   fica `esperando as plantas crescerem` e ele **fica no canteiro**.
   - `modia_stats` → `Trabalho: Cultivar (3, 1.2 min) — esperando as plantas crescerem`.
   - Sem zona definida: `modia_job farm` recusa com "Nenhuma zona de fazenda definida".
   - Sem semente e sem nada crescendo: `Cultivar encerrado (nada para plantar nem colher na zona)`.
4. **Cultivar — colher:** espere a planta crescer (com `Jobs/TempoMaximoPorJob = 0` ele fica
   esperando) → no log `indo colher 'Carrot' a 1,8 m`, `colhi 'Carrot'`, `catei 2 item(ns) da
   colheita` e, a cada 20 itens (`Jobs/FarmDepositarCada`), `colheita na mochila; levando ao bau` (a
   viagem da 2.14 pausa o trabalho e ele volta depois). As **sementes ficam** no inventário
   (`Jobs/FarmManterSementes`).
5. **Cozinhar:** dê **madeira** (combustível) e **carne crua** ao companheiro (jogue no chão +
   `modia_gather Wood 20` / `modia_gather RawMeat 5`), acenda uma **fogueira** com um **espeto** em
   cima (ou um forno de cozinha) e mande `modia_job cook`.
   - Log esperado: `pus 1x 'Wood' no fogo (mantido em 50% do maximo)` → `pus 2x 'RawMeat' na estacao;
     agora e esperar ficar pronto` → `esperando o preparo` → quando ficar pronto:
     `retirei o que ficou pronto da estacao` + `catei 1 item(ns) da estacao`.
   - **Barril de fermentação:** com uma base de bebida (ex.: `MeadBaseHealthMinor`) no inventário e um
     barril vazio por perto, ele põe (`pus 'MeadBaseHealthMinor' no barril de fermentacao`) e, quando
     ficar pronto, torneira (`torneirei o barril de fermentacao`) e cata as garrafas.
   - **Caldeirão:** com os ingredientes de uma receita e o fogo aceso, `fiz 1x 'QueensJam' no caldeirao`.
   - Sem nada para fazer: `Cozinhar encerrado (nada para fazer nas estacoes de cozinha)`.
6. **Fundir:** dê **minério** e **carvão** ao companheiro (`modia_gather CopperOre 20` /
   `modia_gather Coal 20` — carvão é o combustível do forno) e mande `modia_job smelt` perto de um
   **forno**.
   - Log: `pus 10x 'Coal' de combustivel no forno` → `pus 10x 'CopperOre' na fila do forno` →
     `esperando o forno` → quando a fila esvaziar, o jogo larga as barras e ele cata:
     `catei 4 produto(s) da fornalha`.
   - **Carvoeira:** com `Wood` no inventário, ele abastece sem combustível e cata `Coal`.
   - Sem forno por perto: `Fundir encerrado (nada para fazer nas fundicoes)`.
7. **Interrupções:** durante qualquer um dos trabalhos, `modia_gather wood` ou encher a mochila
   **pausa** o trabalho (a viagem ao baú vence) e ele retoma depois; `modia_stop` (ou
   `modia_job stop`) encerra na hora e ele volta a seguir você.

### Teste rápido da base e da casa (Fase 2.17)

1. **Definir a casa:** `modia_home set 20` no meio da sua base → no console
   `Casa definida em X, Z com raio 20 m (gravada em Casa/HomeZona)`. `modia_home` (sem argumento)
   mostra a casa e o estado do cuidado. Confira no log do BepInEx:
   `[ModIA] Casa: ... | preferir bau da casa = True | voltar se perdido = False`.
2. **Depósito em casa:** encha a mochila (ou use `modia_deposit`) com um baú **dentro** da casa e
   outro mais perto **fora** dela → ele vai ao **baú da casa** (o log diz qual baú).
3. **Reparar:** deixe uma parede/piso da base danificada (bata nela algumas vezes, ou espere a chuva
   desgastar) e use `modia_job upkeep` → ele pega o **martelo** (aparece na mão dele), anda até a peça
   e o log mostra `Base: reparei Parede de madeira (stamina -X)`. A peça volta a 100% na hora (o
   `RPC_Repair` do jogo). Teste com uma peça **longe** de qualquer bancada: ele **não** repara e o log
   avisa que falta a estação de craft (como um jogador).
   - Sem martelo no inventário e `Base/FornecerMartelo = true`, o log mostra
     `Item fornecido ao companheiro: 'Hammer'`.
4. **Abastecer fogueira:** deixe uma fogueira quase apagando com **madeira** no inventário dele
   (`modia_gather wood 20`) e `modia_job upkeep` → `Base: pus Nx 'Wood' em Fogueira`. A fogueira sobe
   até ~50% (`Base/FogueiraNivel`) e o fogo não apaga.
5. **Colmeia:** espere a colmeia encher (ou force com `spawn Beehive` e deixe o mel acumular) e
   `modia_job upkeep` → `Base: esvaziei a colmeia` + `Base: catei N mel da colmeia`; o mel aparece no
   inventário dele (`modia_inv`).
6. **Ordenar baús:** espalhe o mesmo item (ex.: `Wood`) em **dois** baús, com mais em um deles, e
   `modia_job upkeep` → o log mostra
   `Base: tirei N Madeira de <baú A> para levar ao bau com mais deles (<baú B>)` e depois
   `Base: pus N Madeira em <baú B>`. Se um baú tiver duas pilhas parciais do mesmo item, ele também
   `juntei N pilha(s)`.
   - `Base/OrdenarItens = Wood` limita a arrumação só à madeira (o resto fica onde está).
7. **Modo contínuo:** `modia_basecare on` → `Modo continuo ligado: ele ja esta cuidando da base`
   (com a casa definida). Ele passa a reparar/abastecer/ordenar sozinho, volta ao centro quando não há
   nada e o `modia_stats` mostra `Cuidado da base: ligado = True, continuo (nesta sessao), tarefas: ...`.
   - `modia_stop` **desliga** o modo contínuo (o console avisa) — use `modia_basecare on` para voltar.
   - Sem casa definida, `modia_basecare on` avisa que precisa do `modia_home set`.
8. **Voltar para casa (opcional):** `Casa/VoltarSePerdido = true` no `.cfg`, reinicie (ou use
   `modia_home set` de novo) e afaste-se muito com o companheiro preso para trás → no log
   `Casa: o companheiro esta a N m da base e longe do jogador; voltando para casa` e, ao chegar,
   `Casa: o companheiro chegou em casa e espera por voce`.
9. **Não quebrou nada:** com o trabalho rodando, `modia_job hunt`, `modia_gather wood` ou um ataque
   **pausam** o cuidado (o log avisa) e ele retoma depois; `Jobs/Ligado = false` ou
   `Base/Ligado = false` fazem o `modia_job upkeep` recusar com o motivo.

### Teste rápido da qualidade de vida (Fase 2.18)

1. **Sentar no fogo:** fique **parado** perto de uma fogueira **acesa** com o companheiro do lado e
   sem nada no comando (sem `modia_job`, sem coleta) → em alguns segundos o log mostra
   `Descanso: o companheiro sentou para descansar (ocioso perto do fogo)` e o HUD
   `Companheiro descansando`. Ele fica **virado para o fogo** e (com o Animator do corpo do jogador) na
   **animação de sentar**. `modia_stats` → `Descanso: sentado descansando, sem descanso`.
   - Depois de ~20 s (`Descanso/TempoParaDescansar`) o log mostra
     `Descanso: o companheiro esta DESCANSADO por N s (conforto X): stamina x2, cura x1,5, skill +0,5`
     e o `modia_stats` passa a `descansado (N s)`.
   - **Levanta sozinho:** ande **para longe** (mais de ~18 m) ou ponha um inimigo perto → o log mostra
     `Descanso: o companheiro levantou (voce se afastou)` (ou `inimigo por perto`) e ele volta a te
     seguir. Se a fogueira **apagar**, o motivo é `a fogueira apagou`.
   - Sem fogueira e **sem** casa definida ele **não** senta (é o esperado); com a casa definida
     (`modia_home set`), ele senta mesmo sem fogueira (`dentro da casa`).
   - `modia_rest sit` manda sentar **agora** (procura fogueira num raio maior) e `modia_rest stop`
     levanta na hora.
2. **Dormir na cama:** construa **duas** camas dentro/ao lado da casa e durma na **sua** (deixe a
   outra **sem dono** ou com você, mas **não** como seu ponto de spawn) → ao **anoitecer**
   (`Descanso/DormirDeNoite`) o log mostra
   `Descanso: indo dormir na cama 'Bed' (e noite e ha uma cama livre, N m)` e depois
   `Descanso: o companheiro se deitou na cama 'Bed'` + HUD `Companheiro dormindo`. Ele fica **deitado
   na cama** (animação de deitar) e **a noite passa** como quando você dorme.
   - **Acorda** ao **amanhecer** (`acordou (amanheceu)`), ou se você levantar antes
     (`acordou (voce saiu da cama)` quando ele foi dormir junto com você), ou se aparecer inimigo, ou
     se você se afastar mais de `Descanso/DistanciaAcordar` (30 m). Ao levantar ele sai **ao lado** da
     cama e fica com o descanso aplicado (`Companheiro descansado (…s)`).
   - **A sua cama é sua:** com **só** a sua cama (a que é o seu ponto de spawn), ele **não** dorme — o
     log avisa que não há cama livre. `modia_rest sleep` responde
     `Nao ha cama LIVRE por perto (a sua cama ... nao conta: construa uma segunda)`.
   - `modia_rest sleep` manda dormir **agora**, mesmo de dia (o sono dura até
     `Descanso/DistanciaAcordar`/tempo do pedido); `modia_rest stop` (ou `modia_stop`) levanta.
3. **Portal:** ponha **dois portais** ligados (com o mesmo nome) longe um do outro (e o companheiro
   do lado de **um** deles) → atravesse o portal. Durante o teleporte o log mostra
   `Portal: voce esta atravessando um portal; o companheiro vai atras.`; quando você chegar do outro
   lado, `Portal: o companheiro atravessou com voce e chegou em X, Z` + HUD
   `Companheiro atravessou o portal com voce` — e ele **aparece do seu lado**.
   - **Não entra em loop:** teleporte de novo para voltar → ele atravessa de novo (uma viagem por
     teleporte). Se o outro lado estiver a menos de ~8 m (portal curto), ele **não** é levado (só
     anda).
   - **Ocupado não segue:** com `modia_job hunt` (ou coleta) rodando, atravesse o portal → o log
     avisa `voce atravessou um portal, mas o companheiro esta ocupado ... ele fica onde esta`.
   - `modia_rest` (sem argumento) mostra `Portal: ligado (ele atravessa com voce).`.
4. **Carroça:** construa uma **carroça** (banco de trabalho: `Cart`), deixe-a **livre** perto do
   companheiro e use `modia_cart pull` → o log mostra `Carroca: indo ate o engate de 'Cart' (N m)`,
   ele **anda até o engate**, se **prende** nela e o log confirma
   `Carroca: o companheiro esta puxando 'Cart' e vai leva-la seguindo voce` + HUD
   `Companheiro puxando a carroca`. Agora **ande** (o companheiro te segue e a **carroça vem atrás**,
   com a linha/aresta do jogo aparecendo).
   - `modia_cart release` (ou `modia_stop`) solta: o log mostra
     `Carroca: soltei a carroca (comando modia_cart release)` e ele volta a te seguir sozinho.
   - Se a carroça ficar **presa demais** (você passar por um lugar apertado), a junta arrebenta e o
     log avisa `Carroca: a carroca se soltou (junta arrebentou ou ela ficou presa)`.
   - Sem carroça no raio (`Carroca/Raio`): `Nao achei carroca livre num raio de 25 m`.
   - `modia_stats` → `Carroca: puxando a carroca.`
5. **Não quebrou nada:** com o companheiro **sentado/dormindo/puxando**, `modia_gather wood` ou
   `modia_job hunt` **levantam** ele (o descanso não sobrevive a um trabalho); `modia_stop` levanta e
   solta a carroça; dispensar (H) e a morte dele encerram o sono (sem deixar a gravidade desligada) e
   soltam a carroça; e o follow/coleta/combate/jobs/casa continuam funcionando como antes.

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
