using System;
using System.Collections;
using System.Collections.Generic; // 用于 Dictionary
using System.IO;
using System.Reflection;
using Duckov.Buffs;
using Duckov.UI.DialogueBubbles;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace DuckSoulsDLC
{
    /// <summary>
    /// [新增] 咒术管理器
    /// 负责所有咒术（Buff）的定义、加载、施法和移除。
    /// </summary>
    public class SpellManager
    {
        // ==================== [咒术定义] ====================
        // --- 铁身躯 (ID: 1) ---
        private const int SPELL_IRON_FLESH_ID = 2025111201;
        private const float STAMINA_COST_IRON_FLESH = 40f;
        private const float DURATION_IRON_FLESH = 15f;
        private const float ARMOR_BONUS_BODY = 2f;
        private const float ARMOR_BONUS_HEAD = 2f;
        private const float SPEED_PENALTY_IRON_FLESH = -0.60f;
        private const float ELECTRIC_RESIST_PENALTY = 0.50f;   // 电击易伤 50%
        private const string ICON_NAME_IRON_FLESH = "铁身躯.png";

        // --- 剧烈出汗 (ID: 2) ---
        private const int SPELL_PROFUSE_SWEAT_ID = 2025111202;
        private const float STAMINA_COST_SWEAT = 30f;
        private const float DURATION_SWEAT = 20f;
        private const float FIRE_RESIST_BONUS = -0.60f;  // 火焰抗性 +60% (承伤率 -60%)
        private const float SPEED_BONUS_SWEAT = 0.10f;   // 移速 +10%
        private const string ICON_NAME_PROFUSE_SWEAT = "剧烈出汗.png";

        // --- 内在潜力 (ID: 3) ---
        private const int SPELL_INNER_POTENTIAL_ID = 2025111203;
        private const float STAMINA_COST_POTENTIAL = 30f;
        private const float DURATION_POTENTIAL = 15f;
        private const float DAMAGE_BONUS = 0.30f;           // 伤害倍率 +30%
        private const float HEALTH_COST_PER_SEC = -2f;      // 每秒 2 点伤害
        private const string ICON_NAME_INNER_POTENTIAL = "内在潜力.png";
        // ==========================================================
        
        private ModBehaviour _modInstance; // 对 ModBehaviour 主实例的引用

        private bool isActivated = false;
        private Sprite ironFleshSpellIcon = null;
        private Sprite profuseSweatSpellIcon = null;
        private Sprite innerPotentialSpellIcon = null;
        private Buff ironFleshBuffTemplate = null;
        private Buff profuseSweatBuffTemplate = null;
        private Buff innerPotentialBuffTemplate = null;
        private CharacterBuffManager buffManager = null;
        private Dictionary<int, Coroutine> activeSpells = new Dictionary<int, Coroutine>();
        private readonly object ironFleshSource = new object();
        private readonly object profuseSweatSource = new object();
        private readonly object innerPotentialSource = new object();
        
        /// <summary>
        /// 构造函数，当 ModBehaviour 创建这个类的实例时调用
        /// </summary>
        public SpellManager(ModBehaviour modInstance)
        {
            _modInstance = modInstance;
        }

        #region Initialization and Lifecycle

        /// <summary>
        /// 由 ModBehaviour.OnAfterSetup() 调用
        /// </summary>
        public void OnAfterSetup()
        {
            _modInstance.StartCoroutine(DelayedInitialization()); // 启动咒术系统
        }

        private IEnumerator DelayedInitialization()
        {
            yield return new WaitForSeconds(0.5f);
            if (!isActivated)
            {
                isActivated = true;
                LoadSpellIcons();

                // 订阅事件
                CharacterMainControl.OnMainCharacterStartUseItem += OnPlayerTryUseItem;

                Debug.Log("[DuckSoulsDLC] 咒术系统已启动。");
            }
        }

        /// <summary>
        /// 由 ModBehaviour.OnBeforeDeactivate() 调用
        /// </summary>
        public void OnBeforeDeactivate()
        {
            if (isActivated)
            {
                isActivated = false;

                // 取消订阅
                CharacterMainControl.OnMainCharacterStartUseItem -= OnPlayerTryUseItem;

                // 确保 Mod 卸载时移除 Buff
                RemoveIronFleshBuff(false);
                RemoveProfuseSweatBuff(false);
                RemoveInnerPotentialBuff(false);

                // 清理 Buff 模板
                if (ironFleshBuffTemplate != null) UnityEngine.Object.Destroy(ironFleshBuffTemplate.gameObject);
                if (profuseSweatBuffTemplate != null) UnityEngine.Object.Destroy(profuseSweatBuffTemplate.gameObject);
                if (innerPotentialBuffTemplate != null) UnityEngine.Object.Destroy(innerPotentialBuffTemplate.gameObject);

                Debug.Log("[DuckSoulsDLC] 咒术系统已关闭。");
            }
        }

        #endregion

        #region Icon and Buff Creation

        private void LoadSpellIcons()
        {
            try
            {
                string assemblyLocation = Assembly.GetExecutingAssembly().Location;
                string modDirectory = Path.GetDirectoryName(assemblyLocation);
                string iconsDirectory = Path.Combine(modDirectory, "icons");

                if (!Directory.Exists(iconsDirectory))
                {
                    Debug.LogWarning($"[DuckSoulsDLC] 图标文件夹未找到: {iconsDirectory}");
                    return;
                }

                ironFleshSpellIcon = LoadIconFromFile(iconsDirectory, ICON_NAME_IRON_FLESH);
                profuseSweatSpellIcon = LoadIconFromFile(iconsDirectory, ICON_NAME_PROFUSE_SWEAT);
                innerPotentialSpellIcon = LoadIconFromFile(iconsDirectory, ICON_NAME_INNER_POTENTIAL);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DuckSoulsDLC] 加载图标时出错: {ex.Message}");
            }
        }

        private Sprite LoadIconFromFile(string iconsDirectory, string fileName)
        {
            string iconPath = Path.Combine(iconsDirectory, fileName);
            if (!File.Exists(iconPath))
            {
                Debug.LogWarning($"[DuckSoulsDLC] 图标文件未找到: {fileName}");
                return null;
            }

            byte[] fileData = File.ReadAllBytes(iconPath);
            Texture2D tex = new Texture2D(2, 2);
            if (tex.LoadImage(fileData))
            {
                Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                Debug.Log($"[DuckSoulsDLC] 成功加载图标: {fileName}");
                return sprite;
            }
            return null;
        }
        
        private Buff GetIronFleshBuffTemplate()
        {
            if (ironFleshBuffTemplate != null) return ironFleshBuffTemplate;
            if (buffManager == null) buffManager = CharacterMainControl.Main?.GetBuffManager();
            if (buffManager == null) return null;

            Sprite iconToUse = ironFleshSpellIcon ?? CreateDummySprite(Color.red);
            ironFleshBuffTemplate = CreateBuffTemplate("铁身躯", "防御提升，移速降低，电击易伤。", iconToUse, 2025111203);
            return ironFleshBuffTemplate;
        }

        private Buff GetProfuseSweatBuffTemplate()
        {
            if (profuseSweatBuffTemplate != null) return profuseSweatBuffTemplate;
            if (buffManager == null) buffManager = CharacterMainControl.Main?.GetBuffManager();
            if (buffManager == null) return null;

            Sprite iconToUse = profuseSweatSpellIcon ?? CreateDummySprite(Color.blue);
            profuseSweatBuffTemplate = CreateBuffTemplate("剧烈出汗", "火焰抗性提升，移速提升。", iconToUse, 2025111204);
            return profuseSweatBuffTemplate;
        }
        
        private Buff GetInnerPotentialBuffTemplate()
        {
            if (innerPotentialBuffTemplate != null) return innerPotentialBuffTemplate;
            if (buffManager == null) buffManager = CharacterMainControl.Main?.GetBuffManager();
            if (buffManager == null) return null;

            Sprite iconToUse = innerPotentialSpellIcon ?? CreateDummySprite(Color.magenta); //
            innerPotentialBuffTemplate = CreateBuffTemplate("内在潜力", "伤害大幅提升，但会持续损失生命。", iconToUse, 2025111205);
            return innerPotentialBuffTemplate;
        }

        private Buff CreateBuffTemplate(string nameKey, string descriptionKey, Sprite icon, int id)
        {
            GameObject gameObject = new GameObject($"ModBuff_{id}");
            gameObject.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            Buff buff = gameObject.AddComponent<Buff>();

            Type typeFromHandle = typeof(Buff);

            try { typeFromHandle.GetField("id", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(buff, id); } catch { }
            try { typeFromHandle.GetField("displayName", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(buff, nameKey); } catch { }
            try { typeFromHandle.GetField("description", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(buff, descriptionKey); } catch { }
            try { typeFromHandle.GetField("icon", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(buff, icon); } catch { }
            try { typeFromHandle.GetField("limitedLifeTime", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(buff, true); } catch { }

            return buff;
        }

        private Sprite CreateDummySprite(Color color)
        {
            int num = 60;
            Texture2D texture2D = new Texture2D(num, num, TextureFormat.ARGB32, false);
            float num2 = (float)num / 2f;
            Vector2 b = new Vector2(num2, num2);
            Color[] array = new Color[num * num];
            for (int i = 0; i < num; i++)
            {
                for (int j = 0; j < num; j++)
                {
                    float num3 = Vector2.Distance(new Vector2((float)j, (float)i), b);
                    if (num3 <= num2 - 1f)
                    {
                        array[i * num + j] = color;
                    }
                    else
                    {
                        array[i * num + j] = Color.clear;
                    }
                }
            }
            texture2D.SetPixels(array);
            texture2D.Apply();
            return Sprite.Create(texture2D, new Rect(0f, 0f, (float)num, (float)num), new Vector2(0.5f, 0.5f));
        }

        #endregion

        #region Spell System Logic

        private void OnPlayerTryUseItem(Item item)
        {
            if (item == null) return;

            switch (item.TypeID)
            {
                case SPELL_IRON_FLESH_ID:
                    TryCastSpell(
                        SPELL_IRON_FLESH_ID,
                        STAMINA_COST_IRON_FLESH,
                        "铁身躯，启动！",
                        _modInstance.StartCoroutine(IronFleshEffect()) // 使用 _modInstance 启动协程
                    );
                    break;

                case SPELL_PROFUSE_SWEAT_ID:
                    TryCastSpell(
                        SPELL_PROFUSE_SWEAT_ID,
                        STAMINA_COST_SWEAT,
                        "我现在汗如雨下！",
                        _modInstance.StartCoroutine(ProfuseSweatEffect()) // 使用 _modInstance 启动协程
                    );
                    break;

                case SPELL_INNER_POTENTIAL_ID:
                    TryCastSpell(
                        SPELL_INNER_POTENTIAL_ID,
                        STAMINA_COST_POTENTIAL,
                        "我感受到了力量！",
                        _modInstance.StartCoroutine(InnerPotentialEffect()) // 使用 _modInstance 启动协程
                    );
                    break;

                default:
                    return;
            }
        }
        
        private void TryCastSpell(int spellId, float staminaCost, string popText, Coroutine effectCoroutine)
        {
            CharacterMainControl.Main?.CurrentAction?.StopAction();

            try
            {
                var player = CharacterMainControl.Main;
                if (player == null) return;
                
                if (buffManager == null) buffManager = player.GetBuffManager();
                
                if (player.CurrentStamina < staminaCost)
                {
                    DialogueBubblesManager.Show("...精力不足...", player.transform, duration: 3f).Forget();
                    return;
                }
                
                if (activeSpells.ContainsKey(spellId))
                {
                    _modInstance.StopCoroutine(activeSpells[spellId]); // 使用 _modInstance 停止协程
                }
                
                player.UseStamina(staminaCost);
                DialogueBubblesManager.Show(popText, player.transform, duration: 3f).Forget();
                
                activeSpells[spellId] = effectCoroutine;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DuckSoulsDLC] 施法时发生严重错误 (ID: {spellId}): {ex.ToString()}");
            }
        }
        
        private IEnumerator IronFleshEffect()
        {
            RemoveIronFleshBuff(false);

            try
            {
                Item playerItem = CharacterMainControl.Main.CharacterItem;
                if (playerItem != null)
                {
                    playerItem.GetStat("BodyArmor".GetHashCode())?.AddModifier(new Modifier(ModifierType.Add, ARMOR_BONUS_BODY, ironFleshSource));
                    playerItem.GetStat("HeadArmor".GetHashCode())?.AddModifier(new Modifier(ModifierType.Add, ARMOR_BONUS_HEAD, ironFleshSource));
                    playerItem.GetStat("WalkSpeed".GetHashCode())?.AddModifier(new Modifier(ModifierType.PercentageAdd, SPEED_PENALTY_IRON_FLESH, ironFleshSource));
                    playerItem.GetStat("RunSpeed".GetHashCode())?.AddModifier(new Modifier(ModifierType.PercentageAdd, SPEED_PENALTY_IRON_FLESH, ironFleshSource));
                    playerItem.GetStat("ElementFactor_Electricity".GetHashCode())?.AddModifier(new Modifier(ModifierType.Add, ELECTRIC_RESIST_PENALTY, ironFleshSource));
                    Debug.Log("[DuckSoulsDLC] '铁身躯' 属性已施加。");
                }
            }
            catch (Exception ex) { Debug.LogError("[DuckSoulsDLC] 施加 '铁身躯' 属性时出错: " + ex.ToString()); }
            
            try
            {
                Buff template = GetIronFleshBuffTemplate();
                if (buffManager != null && template != null)
                {
                    typeof(Buff).GetField("totalLifeTime", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(template, DURATION_IRON_FLESH);
                    buffManager.AddBuff(template, CharacterMainControl.Main, 0);
                }
            }
            catch (Exception ex) { Debug.LogError("[DuckSoulsDLC] 施加 '铁身躯' UI Buff 时出错: " + ex.ToString()); }
            
            yield return new WaitForSeconds(DURATION_IRON_FLESH);
            
            RemoveIronFleshBuff(true);
            activeSpells.Remove(SPELL_IRON_FLESH_ID);
        }
        
        private IEnumerator ProfuseSweatEffect()
        {
            RemoveProfuseSweatBuff(false);
            
            try
            {
                Item playerItem = CharacterMainControl.Main.CharacterItem;
                if (playerItem != null)
                {
                    playerItem.GetStat("ElementFactor_Fire".GetHashCode())?.AddModifier(new Modifier(ModifierType.Add, FIRE_RESIST_BONUS, profuseSweatSource));
                    playerItem.GetStat("WalkSpeed".GetHashCode())?.AddModifier(new Modifier(ModifierType.PercentageAdd, SPEED_BONUS_SWEAT, profuseSweatSource));
                    playerItem.GetStat("RunSpeed".GetHashCode())?.AddModifier(new Modifier(ModifierType.PercentageAdd, SPEED_BONUS_SWEAT, profuseSweatSource));
                    Debug.Log("[DuckSoulsDLC] '剧烈出汗' 属性已施加。");
                }
            }
            catch (Exception ex) { Debug.LogError("[DuckSoulsDLC] 施加 '剧烈出汗' 属性时出错: " + ex.ToString()); }
            
            try
            {
                Buff template = GetProfuseSweatBuffTemplate();
                if (buffManager != null && template != null)
                {
                    typeof(Buff).GetField("totalLifeTime", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(template, DURATION_SWEAT);
                    buffManager.AddBuff(template, CharacterMainControl.Main, 0);
                }
            }
            catch (Exception ex) { Debug.LogError("[DuckSoulsDLC] 施加 '剧烈出汗' UI Buff 时出错: " + ex.ToString()); }
            
            yield return new WaitForSeconds(DURATION_SWEAT);
            
            RemoveProfuseSweatBuff(true);
            activeSpells.Remove(SPELL_PROFUSE_SWEAT_ID);
        }
        
        private IEnumerator InnerPotentialEffect()
        {
            RemoveInnerPotentialBuff(false);
            
            try
            {
                Item playerItem = CharacterMainControl.Main.CharacterItem;
                if (playerItem != null)
                {
                    playerItem.GetStat("MeleeDamageMultiplier".GetHashCode())?.AddModifier(new Modifier(ModifierType.Add, DAMAGE_BONUS, innerPotentialSource));
                    Debug.Log("[DuckSoulsDLC] '内在潜力' 属性已施加。");
                }
            }
            catch (Exception ex) { Debug.LogError("[DuckSoulsDLC] 施加 '内在潜力' 属性时出错: " + ex.ToString()); }
            
            try
            {
                Buff template = GetInnerPotentialBuffTemplate();
                if (buffManager != null && template != null)
                {
                    typeof(Buff).GetField("totalLifeTime", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(template, DURATION_POTENTIAL);
                    buffManager.AddBuff(template, CharacterMainControl.Main, 0);
                }
            }
            catch (Exception ex) { Debug.LogError("[DuckSoulsDLC] 施加 '内在潜力' UI Buff 时出错: " + ex.ToString()); }
            
            float elapsedTime = 0f;
            while (elapsedTime < DURATION_POTENTIAL)
            {
                yield return new WaitForSeconds(1.0f);
                
                try
                {
                    CharacterMainControl.Main?.Health?.AddHealth(HEALTH_COST_PER_SEC);
                }
                catch (Exception ex) { Debug.LogError("[DuckSoulsDLC] '内在潜力' DoT 扣血时出错: " + ex.ToString()); }

                elapsedTime += 1.0f;
                
                if (!activeSpells.ContainsKey(SPELL_INNER_POTENTIAL_ID))
                {
                    Debug.Log("[DuckSoulsDLC] '内在潜力' 协程被提前终止。");
                    yield break;
                }
            }
            
            RemoveInnerPotentialBuff(true);
            activeSpells.Remove(SPELL_INNER_POTENTIAL_ID);
        }
        
        private void RemoveIronFleshBuff(bool showBubble = true)
        {
            try
            {
                Item playerItem = CharacterMainControl.Main?.CharacterItem;
                if (playerItem != null)
                {
                    playerItem.GetStat("BodyArmor".GetHashCode())?.RemoveAllModifiersFromSource(ironFleshSource);
                    playerItem.GetStat("HeadArmor".GetHashCode())?.RemoveAllModifiersFromSource(ironFleshSource);
                    playerItem.GetStat("WalkSpeed".GetHashCode())?.RemoveAllModifiersFromSource(ironFleshSource);
                    playerItem.GetStat("RunSpeed".GetHashCode())?.RemoveAllModifiersFromSource(ironFleshSource);
                    playerItem.GetStat("ElementFactor_Electricity".GetHashCode())?.RemoveAllModifiersFromSource(ironFleshSource);

                    if (showBubble) DialogueBubblesManager.Show("...铁身躯效果消失...", CharacterMainControl.Main.transform, duration: 3f).Forget();
                }
            }
            catch (Exception ex) { Debug.LogError("[DuckSoulsDLC] 移除 '铁身躯' 属性时出错: " + ex.ToString()); }
            
            try
            {
                if (buffManager != null && ironFleshBuffTemplate != null)
                {
                    buffManager.RemoveBuff(ironFleshBuffTemplate.ID, false);
                }
            }
            catch (Exception ex) { Debug.LogError("[DuckSoulsDLC] 移除 '铁身躯' UI Buff 时出错: " + ex.ToString()); }
        }
        
        private void RemoveProfuseSweatBuff(bool showBubble = true)
        {
            try
            {
                Item playerItem = CharacterMainControl.Main?.CharacterItem;
                if (playerItem != null)
                {
                    playerItem.GetStat("ElementFactor_Fire".GetHashCode())?.RemoveAllModifiersFromSource(profuseSweatSource);
                    playerItem.GetStat("WalkSpeed".GetHashCode())?.RemoveAllModifiersFromSource(profuseSweatSource);
                    playerItem.GetStat("RunSpeed".GetHashCode())?.RemoveAllModifiersFromSource(profuseSweatSource);

                    if (showBubble) DialogueBubblesManager.Show("...汗停了...", CharacterMainControl.Main.transform, duration: 3f).Forget();
                }
            }
            catch (Exception ex) { Debug.LogError("[DuckSoulsDLC] 移除 '剧烈出汗' 属性时出错: " + ex.ToString()); }
            
            try
            {
                if (buffManager != null && profuseSweatBuffTemplate != null)
                {
                    buffManager.RemoveBuff(profuseSweatBuffTemplate.ID, false);
                }
            }
            catch (Exception ex) { Debug.LogError("[DuckSoulsDLC] 移除 '剧烈出汗' UI Buff 时出错: " + ex.ToString()); }
        }
        
        private void RemoveInnerPotentialBuff(bool showBubble = true)
        {
            try
            {
                Item playerItem = CharacterMainControl.Main?.CharacterItem;
                if (playerItem != null)
                {
                    playerItem.GetStat("MeleeDamageMultiplier".GetHashCode())?.RemoveAllModifiersFromSource(innerPotentialSource);
                    if (showBubble) DialogueBubblesManager.Show("...潜力消退...", CharacterMainControl.Main.transform, duration: 3f).Forget();
                }
            }
            catch (Exception ex) { Debug.LogError("[DuckSoulsDLC] 移除 '内在潜力' 属性时出错: " + ex.ToString()); }
            
            try
            {
                if (buffManager != null && innerPotentialBuffTemplate != null)
                {
                    buffManager.RemoveBuff(innerPotentialBuffTemplate.ID, false);
                }
            }
            catch (Exception ex) { Debug.LogError("[DuckSoulsDLC] 移除 '内在潜力' UI Buff 时出错: " + ex.ToString()); }
        }

        #endregion
    }
}