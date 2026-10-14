using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// Comida do companheiro (Fase 2.13): ele <b>come sozinho</b> usando a comida do próprio
    /// inventário e ganha os mesmos bônus do jogador (vida máxima, stamina máxima e regeneração de
    /// vida), respeitando os tempos de queima do Valheim.
    ///
    /// <para>
    /// <b>Como o Valheim 1.0.17 trata comida (confirmado no <c>assembly_valheim</c>).</b> Os
    /// <i>slots</i> de comida (<c>m_foods</c>, <c>m_foodUpdateTimer</c>, <c>GetTotalFoodValue</c>,
    /// <c>EatFood</c>, <c>UpdateFood</c>) são <b>privados do <c>Player</c></b> — e
    /// <c>Humanoid.ConsumeItem(inventory, item, checkWorldLevel)</c> é <c>virtual</c> com um corpo
    /// <b>vazio</b> na base (só chama <c>CanConsumeItem</c> e devolve <c>false</c>); quem come de
    /// verdade é <c>Player.ConsumeItem</c> → <c>EatFood</c>. Ou seja: <b>não existe caminho pronto
    /// para um NPC</b> — e copiar o do <c>Player</c> não serve, porque ele escreve no perfil do
    /// jogador (<c>Game.instance.GetPlayerProfile().IncrementStatFoodEaten</c>), na
    /// <c>MessageHud</c> e nos campos <c>m_baseHP</c>/<c>m_maxStamina</c> que só o jogador tem.
    /// </para>
    ///
    /// <para>
    /// <b>O que fazemos então:</b> mantemos 3 slots iguais aos do jogo
    /// (<c>CompanionFood.MaxSlots</c> — o jogador também tem 3) e reproduzimos <i>as contas</i> do
    /// <c>Player.UpdateFood</c>:
    /// </para>
    /// <list type="number">
    ///   <item><description><b>Queima:</b> o tempo de cada comida cai 1 s a cada
    ///   <c>1 / Game.m_foodRate</c> segundos de jogo (o mesmo acumulador <c>m_foodUpdateTimer</c>).</description></item>
    ///   <item><description><b>Bônus decrescente:</b> o valor entregue é
    ///   <c>base × pow(clamp01(tempo restante / tempo total), 0.3)</c> — a mesma curva do jogo, então
    ///   comida no fim da queima entrega quase nada.</description></item>
    ///   <item><description><b>Vida máxima:</b> <c>Character.SetMaxHealth(base + bônus)</c>, exatamente
    ///   como o <c>Player</c> (o HP atual é só aparado no novo teto; comer <b>não</b> cura).</description></item>
    ///   <item><description><b>Stamina máxima:</b> vai para o <see cref="CompanionStamina"/>, que soma
    ///   o mesmo bônus ao teto dele.</description></item>
    ///   <item><description><b>Regeneração:</b> a cada 10 s (o intervalo do jogo) ele recebe
    ///   <c>Σ m_foodRegen</c> de vida, passando pelo multiplicador de efeitos de status
    ///   (<c>SEMan.ModifyHealthRegen</c>) — é o que faz "Descansado" curar mais e "Molhado/Congelado"
    ///   curar menos, como no jogador.</description></item>
    ///   <item><description><b>Efeito de status da comida:</b> <c>SEMan.AddStatusEffect(m_consumeStatusEffect)</c>
    ///   (sopas, tortas, hidromel-base...) — e a comida é recusada se o efeito (ou a categoria dele)
    ///   já estiver ativo, como no jogo.</description></item>
    ///   <item><description><b>Animacão/som:</b> <c>m_zanim.SetTrigger("eat")</c>,
    ///   <c>Humanoid.m_consumeItemEffects</c> e <c>SetUseHandVisual</c> (o item aparece na mão dele
    ///   por <c>m_foodEatAnimTime</c>) — o mesmo que <c>Humanoid.UseItem</c> faz.</description></item>
    /// </list>
    ///
    /// <para>
    /// <b>Sem desperdício.</b> Ele só come quando faz sentido: com um slot livre (manter as 3 comidas
    /// ativas, como um jogador), ou — com os 3 cheios — apenas se a vida ou a stamina estiverem abaixo
    /// do limiar <b>e</b> alguma comida já tiver passado de metade da queima (a regra
    /// <c>CanEatAgain</c> do jogo: antes disso o jogo recusaria a recarga). Na hora de escolher, ele
    /// prefere a comida que cobre a necessidade (mais vida quando a vida está baixa, mais stamina
    /// quando a stamina está baixa) e, quando não há necessidade nenhuma, a <b>mais barata</b> — para
    /// não queimar o banquete bom à toa.
    /// </para>
    ///
    /// <para>
    /// Roda do <see cref="CompanionAI.UpdateAI"/> (~20 Hz), só no dono do ZDO, sem alocação por frame
    /// (o inventário só é varrido quando chega a hora de comer, com intervalo configurável).
    /// </para>
    /// </summary>
    internal sealed class CompanionFood : MonoBehaviour
    {
        /// <summary>Slots de comida simultâneos (o jogador também tem 3).</summary>
        internal const int MaxSlots = 3;

        /// <summary>Intervalo (s) entre os ticks de regeneração de vida da comida — o do jogo é 10.</summary>
        private const float RegenInterval = 10f;

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<float> _healthThreshold;
        private static ConfigEntry<float> _staminaThreshold;
        private static ConfigEntry<float> _checkInterval;

        /// <summary>Uma comida ativa num slot (o "Food" do <c>Player</c>).</summary>
        private struct FoodEffect
        {
            internal string Prefab;
            internal string Name;
            internal float Health;
            internal float Stamina;
            internal float Eitr;
            internal float Regen;
            internal float Total;
            internal float Remaining;

            internal bool Active
            {
                get { return Remaining > 0f && !string.IsNullOrEmpty(Name); }
            }
        }

        private readonly FoodEffect[] _slots = new FoodEffect[MaxSlots];

        private Character _character;
        private Humanoid _humanoid;
        private CompanionStamina _stamina;

        private float _burnTimer;
        private float _regenTimer;
        private float _checkTimer;
        private float _baseHealth = -1f;
        private float _appliedMaxHealth = -1f;
        private int _activeCount;

        /// <summary>Comer automaticamente ligado (config <c>Comida/ComerAutomaticamente</c>).</summary>
        internal static bool Enabled => _enabled == null || _enabled.Value;

        private static float HealthThreshold =>
            _healthThreshold != null ? Mathf.Clamp(_healthThreshold.Value, 0f, 100f) : 50f;

        private static float StaminaThreshold =>
            _staminaThreshold != null ? Mathf.Clamp(_staminaThreshold.Value, 0f, 100f) : 30f;

        private static float CheckInterval =>
            _checkInterval != null ? Mathf.Clamp(_checkInterval.Value, 1f, 60f) : 5f;

        /// <summary>Bônus de vida somado das comidas ativas (já com a curva de queima).</summary>
        internal float TotalHealthBonus
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < MaxSlots; i++)
                {
                    if (_slots[i].Active)
                    {
                        total += Scaled(_slots[i].Health, _slots[i]);
                    }
                }

                return total;
            }
        }

        /// <summary>Bônus de stamina somado das comidas ativas (lido pelo <see cref="CompanionStamina"/>).</summary>
        internal float TotalStaminaBonus
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < MaxSlots; i++)
                {
                    if (_slots[i].Active)
                    {
                        total += Scaled(_slots[i].Stamina, _slots[i]);
                    }
                }

                return total;
            }
        }

        /// <summary>Quantas comidas estão ativas agora.</summary>
        internal int ActiveCount => _activeCount;

        internal static void Configure(
            ConfigEntry<bool> enabled,
            ConfigEntry<float> healthThreshold,
            ConfigEntry<float> staminaThreshold,
            ConfigEntry<float> checkInterval)
        {
            _enabled = enabled;
            _healthThreshold = healthThreshold;
            _staminaThreshold = staminaThreshold;
            _checkInterval = checkInterval;

            Plugin.Log.LogInfo(
                $"[ModIA] Comida: comer sozinho = {Enabled} | limiar de vida = " +
                $"{HealthThreshold:0}% | limiar de stamina = {StaminaThreshold:0}% | " +
                $"verificacao a cada {CheckInterval:0.#}s.");
        }

        private void Awake()
        {
            ResolveDependencies();
        }

        private void ResolveDependencies()
        {
            if (_character == null)
            {
                _character = GetComponent<Character>();
            }

            if (_humanoid == null)
            {
                _humanoid = GetComponent<Humanoid>();
            }

            if (_stamina == null)
            {
                _stamina = GetComponent<CompanionStamina>();
            }
        }

        /// <summary>
        /// Vida base do corpo (sem comida) — o equivalente do <c>Player.m_baseHP</c>. É capturada
        /// <b>na primeira</b> aplicação de bônus (o companheiro acabou de nascer, então o valor é o
        /// de fábrica) e guardada: a partir daí a vida máxima é sempre base + comida, sem acumular.
        /// </summary>
        private float BaseHealth
        {
            get
            {
                if (_baseHealth < 0f && _character != null)
                {
                    _baseHealth = Mathf.Max(1f, _character.GetMaxHealth());
                }

                return _baseHealth > 0f ? _baseHealth : 25f;
            }
        }

        /// <summary>A curva de queima do jogo: <c>pow(clamp01(restante / total), 0.3)</c>.</summary>
        private static float Scaled(float baseBonus, FoodEffect food)
        {
            if (baseBonus <= 0f)
            {
                return 0f;
            }

            float total = Mathf.Max(1f, food.Total);
            float fraction = Mathf.Clamp01(food.Remaining / total);
            return baseBonus * Mathf.Pow(fraction, 0.3f);
        }

        // ---------------------------------------------------------------------
        // Ciclo (chamado pelo CompanionAI, só no dono do ZDO)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Queima as comidas, aplica os bônus, regenera vida e (quando chega a hora) decide se come.
        /// </summary>
        internal void Tick(float dt)
        {
            ResolveDependencies();

            if (_character == null || _humanoid == null || _character.IsDead())
            {
                return;
            }

            if (!Enabled)
            {
                if (_activeCount > 0)
                {
                    Clear("comer automaticamente desligado na config");
                }

                return;
            }

            // Queima: 1 s de comida a cada 1/Game.m_foodRate segundo (o acumulador do Player).
            _burnTimer += dt * Game.m_foodRate;
            if (_burnTimer >= 1f)
            {
                _burnTimer -= 1f;
                BurnOneSecond();
            }

            _regenTimer += dt;
            if (_regenTimer >= RegenInterval)
            {
                _regenTimer = 0f;
                ApplyFoodRegen();
            }

            _checkTimer -= dt;
            if (_checkTimer <= 0f)
            {
                _checkTimer = CheckInterval;
                TryEat();
            }
        }

        /// <summary>Passa 1 s de queima em cada comida ativa e reaplica os bônus (que encolhem).</summary>
        private void BurnOneSecond()
        {
            bool changed = false;
            for (int i = 0; i < MaxSlots; i++)
            {
                if (!_slots[i].Active)
                {
                    continue;
                }

                _slots[i].Remaining -= 1f;
                changed = true;

                if (_slots[i].Remaining <= 0f)
                {
                    Plugin.Log.LogInfo(
                        $"[ModIA] Comida: acabou o efeito de '{_slots[i].Name}' (slot {i}); " +
                        "o companheiro vai comer de novo.");
                    _slots[i] = default;
                }
            }

            if (changed)
            {
                ApplyBonuses();
            }
        }

        /// <summary>
        /// Recalcula a vida máxima (e o teto de stamina) a partir das comidas ativas. Só escreve no
        /// ZDO quando o valor muda de verdade (a queima roda 1×/s).
        /// </summary>
        private void ApplyBonuses()
        {
            int active = 0;
            for (int i = 0; i < MaxSlots; i++)
            {
                if (_slots[i].Active)
                {
                    active++;
                }
            }

            _activeCount = active;

            float maxHealth = BaseHealth + TotalHealthBonus;
            if (Mathf.Abs(maxHealth - _appliedMaxHealth) >= 0.5f)
            {
                _appliedMaxHealth = maxHealth;
                _character.SetMaxHealth(maxHealth);
            }

            if (_stamina != null)
            {
                _stamina.RefreshMax();
            }
        }

        /// <summary>Regeneração de vida da comida (Σ <c>m_foodRegen</c>) — como o <c>Player.UpdateFood</c>.</summary>
        private void ApplyFoodRegen()
        {
            float regen = 0f;
            for (int i = 0; i < MaxSlots; i++)
            {
                if (_slots[i].Active)
                {
                    regen += _slots[i].Regen;
                }
            }

            if (regen <= 0f)
            {
                return;
            }

            SEMan seman = _character.GetSEMan();
            if (seman != null)
            {
                float multiplier = 1f;
                seman.ModifyHealthRegen(ref multiplier);
                regen *= multiplier;
            }

            // showText: false — número flutuante de cura é coisa de jogador.
            _character.Heal(regen, showText: false);
        }

        // ---------------------------------------------------------------------
        // Comer
        // ---------------------------------------------------------------------

        /// <summary>
        /// Decide se/como comer. Só varre o inventário aqui (a cada <c>Comida/IntervaloVerificacao</c>,
        /// 5 s por padrão) e só consome um item por vez.
        /// </summary>
        private void TryEat()
        {
            if (_humanoid.InAttack() || _humanoid.InDodge() || _character.IsStaggering())
            {
                return;
            }

            Inventory inventory = _humanoid.GetInventory();
            if (inventory == null)
            {
                return;
            }

            bool needHealth = HealthPercent() * 100f < HealthThreshold;
            bool needStamina = StaminaPercent() * 100f < StaminaThreshold;
            bool hasFreeSlot = HasFreeSlot();
            bool canRefresh = HasRefreshableSlot();

            // Sem slot livre e sem necessidade: não come nada (não desperdiça comida).
            if (!hasFreeSlot && !canRefresh)
            {
                return;
            }

            if (!hasFreeSlot && !needHealth && !needStamina)
            {
                return;
            }

            ItemDrop.ItemData chosen = ChooseFood(inventory, needHealth, needStamina);
            if (chosen == null)
            {
                return;
            }

            int slot = FindSlotFor(chosen);
            if (slot < 0)
            {
                return;
            }

            Consume(inventory, chosen, slot);
        }

        private float HealthPercent()
        {
            float max = _character.GetMaxHealth();
            return max > 0f ? Mathf.Clamp01(_character.GetHealth() / max) : 1f;
        }

        private float StaminaPercent()
        {
            return _stamina != null ? _stamina.GetStaminaPercentage() : 1f;
        }

        private bool HasFreeSlot()
        {
            for (int i = 0; i < MaxSlots; i++)
            {
                if (!_slots[i].Active)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Alguma comida já passou de metade da queima (a regra <c>CanEatAgain</c> do jogo).</summary>
        private bool HasRefreshableSlot()
        {
            for (int i = 0; i < MaxSlots; i++)
            {
                if (CanEatAgain(i))
                {
                    return true;
                }
            }

            return false;
        }

        private bool CanEatAgain(int slot)
        {
            FoodEffect food = _slots[slot];
            if (!food.Active)
            {
                return false;
            }

            return food.Remaining < Mathf.Max(1f, food.Total) * 0.5f;
        }

        /// <summary>
        /// Escolhe a comida do inventário. Com necessidade (vida/stamina baixas) procura a que cobre
        /// a necessidade; sem necessidade nenhuma, procura a <b>mais barata</b> — a comida boa fica
        /// guardada para quando fizer falta.
        /// </summary>
        private ItemDrop.ItemData ChooseFood(Inventory inventory, bool needHealth, bool needStamina)
        {
            List<ItemDrop.ItemData> items = inventory.GetAllItems();
            ItemDrop.ItemData best = null;
            float bestScore = 0f;
            bool anyNeed = needHealth || needStamina;

            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (!IsFoodItem(item) || !CanConsume(item))
                {
                    continue;
                }

                float score;
                if (anyNeed)
                {
                    // Peso maior para o que está faltando (vida baixa → comida de vida).
                    float hpWeight = needHealth ? 3f : 1f;
                    float staminaWeight = needStamina ? 3f : 1f;
                    score = item.m_shared.m_food * hpWeight +
                            item.m_shared.m_foodStamina * staminaWeight;
                }
                else
                {
                    // Nada faltando: prefere a mais barata (menor bônus somado) — evita queimar o
                    // banquete bom só para manter os slots cheios.
                    score = -(item.m_shared.m_food + item.m_shared.m_foodStamina);
                }

                if (best == null || score > bestScore)
                {
                    best = item;
                    bestScore = score;
                }
            }

            return best;
        }

        /// <summary>
        /// Slot de destino: 1) a mesma comida já ativa e recarregável (o jogo "recome a comida"
        /// nesse caso); 2) um slot vazio; 3) a comida mais esgotada que já pode ser recarregada.
        /// </summary>
        private int FindSlotFor(ItemDrop.ItemData item)
        {
            string name = item.m_shared.m_name;

            for (int i = 0; i < MaxSlots; i++)
            {
                if (_slots[i].Active && _slots[i].Name == name && CanEatAgain(i))
                {
                    return i;
                }
            }

            for (int i = 0; i < MaxSlots; i++)
            {
                if (!_slots[i].Active)
                {
                    return i;
                }
            }

            int mostDepleted = -1;
            float lowest = float.MaxValue;
            for (int i = 0; i < MaxSlots; i++)
            {
                if (CanEatAgain(i) && _slots[i].Remaining < lowest)
                {
                    lowest = _slots[i].Remaining;
                    mostDepleted = i;
                }
            }

            return mostDepleted;
        }

        /// <summary>
        /// Consome o item: tira do inventário, aplica o efeito de status, dispara a animação/som e
        /// registra o slot — a versão para NPC do <c>Player.EatFood</c> + <c>Player.ConsumeItem</c>.
        /// </summary>
        private void Consume(Inventory inventory, ItemDrop.ItemData item, int slot)
        {
            string prefabName = item.m_dropPrefab != null
                ? Utils.GetPrefabName(item.m_dropPrefab)
                : item.m_shared.m_name;

            _slots[slot] = new FoodEffect
            {
                Prefab = prefabName,
                Name = item.m_shared.m_name,
                Health = item.m_shared.m_food,
                Stamina = item.m_shared.m_foodStamina,
                Eitr = item.m_shared.m_foodEitr,
                Regen = item.m_shared.m_foodRegen,
                Total = Mathf.Max(1f, item.m_shared.m_foodBurnTime),
                Remaining = Mathf.Max(1f, item.m_shared.m_foodBurnTime)
            };

            inventory.RemoveOneItem(item);

            SEMan seman = _character.GetSEMan();
            if (seman != null && item.m_shared.m_consumeStatusEffect != null)
            {
                seman.AddStatusEffect(item.m_shared.m_consumeStatusEffect, resetTime: true, 0, 0f, -1);
            }

            PlayEatFeedback(item);

            ApplyBonuses();

            Plugin.Log.LogInfo(
                $"[ModIA] Comida: o companheiro comeu '{prefabName}' (slot {slot}) — " +
                $"+{item.m_shared.m_food:0} vida, +{item.m_shared.m_foodStamina:0} stamina, " +
                $"regen {item.m_shared.m_foodRegen:0.#}/10s, queima " +
                $"{item.m_shared.m_foodBurnTime:0}s " +
                $"(comidas ativas: {_activeCount}/{MaxSlots}).");
        }

        /// <summary>
        /// O "como" visual: gatilho <c>eat</c> no animator sincronizado, efeito de consumo e o item
        /// na mão pelo tempo de animação — o mesmo trio que <c>Humanoid.UseItem</c> faz para o jogador.
        /// </summary>
        private void PlayEatFeedback(ItemDrop.ItemData item)
        {
            if (_character.m_zanim != null)
            {
                _character.m_zanim.SetTrigger("eat");
            }

            if (_humanoid.m_consumeItemEffects != null)
            {
                _humanoid.m_consumeItemEffects.Create(
                    transform.position, Quaternion.identity, null, 1f, -1, _character.GetZDOID());
            }

            ObjectDB database = ObjectDB.instance;
            GameObject prefab = null;
            if (database != null && database.TryGetItemPrefab(item.m_shared, out prefab) && prefab != null)
            {
                _humanoid.SetUseHandVisual(prefab, item.m_shared.m_foodEatAnimTime);
            }
        }

        // ---------------------------------------------------------------------
        // Regras de comida
        // ---------------------------------------------------------------------

        /// <summary>
        /// Comida de verdade: consumível com bônus de <b>vida ou stamina</b> — o mesmo filtro do
        /// <c>Player</c> (<c>ConsumeItem</c> só chama <c>EatFood</c> quando <c>m_food &gt; 0</c>).
        /// Hidromel/poções (sem bônus, só efeito de status) e comida <b>só de eitr</b> ficam de fora:
        /// o companheiro não tem eitr, então consumi-la seria desperdício puro.
        /// </summary>
        internal static bool IsFoodItem(ItemDrop.ItemData item)
        {
            if (item == null || item.m_shared == null)
            {
                return false;
            }

            if (item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Consumable)
            {
                return false;
            }

            return item.m_shared.m_food > 0f || item.m_shared.m_foodStamina > 0f;
        }

        /// <summary>Pode consumir agora: não está ativa em outro slot e o efeito de status não conflita.</summary>
        private bool CanConsume(ItemDrop.ItemData item)
        {
            string name = item.m_shared.m_name;

            for (int i = 0; i < MaxSlots; i++)
            {
                if (_slots[i].Active && _slots[i].Name == name && !CanEatAgain(i))
                {
                    return false;
                }
            }

            StatusEffect effect = item.m_shared.m_consumeStatusEffect;
            if (effect == null)
            {
                return true;
            }

            SEMan seman = _character.GetSEMan();
            if (seman == null)
            {
                return true;
            }

            if (seman.HaveStatusEffect(effect))
            {
                return false;
            }

            return string.IsNullOrEmpty(effect.m_category) ||
                   !seman.HaveStatusEffectCategory(effect.m_category);
        }

        // ---------------------------------------------------------------------
        // Snapshot / limpeza
        // ---------------------------------------------------------------------

        /// <summary>Preenche a lista de comidas do snapshot (só as ativas).</summary>
        internal void CaptureState(List<CompanionFoodState> into)
        {
            if (into == null)
            {
                return;
            }

            into.Clear();
            for (int i = 0; i < MaxSlots; i++)
            {
                if (!_slots[i].Active)
                {
                    continue;
                }

                CompanionFoodState record = new CompanionFoodState();
                record.Prefab = _slots[i].Prefab ?? string.Empty;
                record.Name = _slots[i].Name;
                record.Health = _slots[i].Health;
                record.Stamina = _slots[i].Stamina;
                record.Eitr = _slots[i].Eitr;
                record.Regen = _slots[i].Regen;
                record.Remaining = _slots[i].Remaining;
                record.Total = _slots[i].Total;
                into.Add(record);
            }
        }

        /// <summary>
        /// Devolve as comidas guardadas, com o tempo que ainda faltava queimar (o companheiro volta
        /// "alimentado", não de barriga cheia de graça).
        /// </summary>
        internal void RestoreState(List<CompanionFoodState> records)
        {
            if (records == null || records.Count == 0)
            {
                return;
            }

            ResolveDependencies();

            int restored = 0;
            for (int i = 0; i < records.Count && i < MaxSlots; i++)
            {
                CompanionFoodState record = records[i];
                if (record == null || string.IsNullOrEmpty(record.Name))
                {
                    continue;
                }

                float total = Mathf.Max(1f, record.Total);
                float remaining = Mathf.Clamp(record.Remaining, 1f, total);

                _slots[i] = new FoodEffect
                {
                    Prefab = record.Prefab,
                    Name = record.Name,
                    Health = record.Health,
                    Stamina = record.Stamina,
                    Eitr = record.Eitr,
                    Regen = record.Regen,
                    Total = total,
                    Remaining = remaining
                };

                restored++;
            }

            ApplyBonuses();

            Plugin.Log.LogInfo(
                $"[ModIA] Comida restaurada do snapshot: {restored} comida(s) ainda ativa(s).");
        }

        /// <summary>
        /// Limpa as comidas (dispensa, morte ou config desligada): os bônus caem e a vida máxima
        /// volta ao valor base.
        /// </summary>
        internal void Clear(string reason)
        {
            bool had = _activeCount > 0;
            for (int i = 0; i < MaxSlots; i++)
            {
                _slots[i] = default;
            }

            _activeCount = 0;

            if (_character != null && _baseHealth > 0f)
            {
                _appliedMaxHealth = BaseHealth;
                _character.SetMaxHealth(BaseHealth);
            }

            if (_stamina != null)
            {
                _stamina.ClearBonuses();
            }

            if (had)
            {
                Plugin.Log.LogInfo($"[ModIA] Comida: efeitos encerrados ({reason}).");
            }
        }

        /// <summary>Linha legível do que ele tem no corpo (menu/console).</summary>
        internal string Describe()
        {
            if (_activeCount == 0)
            {
                return "nada";
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            for (int i = 0; i < MaxSlots; i++)
            {
                if (!_slots[i].Active)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(_slots[i].Name);
                builder.Append(" (");
                builder.Append(_slots[i].Remaining.ToString("0"));
                builder.Append("s)");
            }

            return builder.ToString();
        }
    }
}
