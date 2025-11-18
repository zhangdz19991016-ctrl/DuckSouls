using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Duckov.UI.DialogueBubbles;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Text; 
using System.Collections.Generic; 

// [!! 新增 !!] 引用 Buff 系统
using Duckov.Buffs; 

// 确保命名空间是 DuckSoulsDLC
namespace DuckSoulsDLC
{
    public class WeaponSkillManager
    {
        private ModBehaviour _modInstance;
        private Coroutine _updateLoopCoroutine;
        private bool isActivated = false;

        // ===================================================================
        // [!!               技能 1: EarthSeeker                 !!]
        // ===================================================================
        
        private const int WEAPON_ID_EARTHSEEKER = 202511034; 
        private const float STAMINA_COST_EARTHSEEKER = 25f; 

        // [!! 敌人 Debuff !!]
        private const float SKILL_RADIUS_EARTHSEEKER = 15f;            
        private const float SKILL_DURATION_EARTHSEEKER = 10.0f;        
        private const float SPEED_PENALTY_EARTHSEEKER = -0.50f;      
        private const float DAMAGE_PER_SEC_EARTHSEEKER = 3f;           

        // [!! 玩家 Self-Buff !!]
        private const float SELF_BUFF_DURATION_EARTHSEEKER = 10.0f;    
        private const float SELF_BUFF_SPEED_BONUS_EARTHSEEKER = 0.10f; 
        private const float SELF_BUFF_DAMAGE_BONUS_EARTHSEEKER = 0.10f;
        // ===================================================================


        // --- 属性常量 ---
        private const string STAT_WALK_SPEED = "WalkSpeed";
        private const string STAT_RUN_SPEED = "RunSpeed";
        private const string STAT_MELEE_DAMAGE = "MeleeDamageMultiplier";

        // --- Buff 协程管理 ---
        private readonly object selfBuffSource = new object();
        private Coroutine _selfBuffCoroutine = null;

        // --- 日志系统 ---
        private static string logFilePath = null;
        private static bool logInit = false;
        
        private Collider[] _hitsBuffer = new Collider[64];

        // ===================================================================
        // [!!               UI Buff 系统                 !!]
        // ===================================================================
        private const string ICON_NAME_EARTHSEEKER = "战鸭咆哮.png";
        private const int BUFF_ID_EARTHSEEKER = 2025111210; // (必须是唯一的 ID)
        
        private Sprite earthSeekerBuffIcon = null;
        private Buff earthSeekerBuffTemplate = null;
        private CharacterBuffManager buffManager = null;
        // ===================================================================


        public WeaponSkillManager(ModBehaviour modInstance)
        {
            _modInstance = modInstance;
            // [!! 关键修复 !!]
            // 构造函数现在是空的。我们不在这个时候加载任何东西。
            // Icon 和 Log 的加载已移至 OnAfterSetup()
        }
        
        private void InitializeLogging()
        {
            if (logInit) return;
            try
            {
                string assemblyLocation = Assembly.GetExecutingAssembly().Location;
                string modDirectory = Path.GetDirectoryName(assemblyLocation);
                string logFolderPath = Path.Combine(modDirectory, "logs");
                Directory.CreateDirectory(logFolderPath);
                logFilePath = Path.Combine(logFolderPath, "DuckSoulsDLC_Skill.log");
                File.WriteAllText(logFilePath, $"--- [DuckSoulsDLC] 日志会话开始于 {DateTime.Now} ---\n");
                logInit = true;
            }
            catch (Exception ex) { Debug.LogError($"[DuckSoulsDLC] 创建日志文件失败: {ex.Message}"); }
        }

        #region Initialization and Lifecycle

        public void OnAfterSetup()
        {
            if (isActivated) return;
            isActivated = true;
            
            // [!! 关键修复 !!]
            // 我们现在在这里初始化日志和图标
            // 这确保了游戏已完全加载，路径是正确的
            InitializeLogging();
            LoadBuffIcons();
            
            _updateLoopCoroutine = _modInstance.StartCoroutine(SkillCheckLoop());
            WriteToLog("WeaponSkillManager.OnAfterSetup() 成功运行。");
            WriteToLog("按键检测协程 SkillCheckLoop 已启动。");
        }

        public void OnBeforeDeactivate()
        {
            if (!isActivated) return;
            isActivated = false;
            
            if (_updateLoopCoroutine != null)
            {
                _modInstance.StopCoroutine(_updateLoopCoroutine);
            }
            
            // 确保 Mod 关闭时自我 Buff (包括UI) 被移除
            if (_selfBuffCoroutine != null)
            {
                _modInstance.StopCoroutine(_selfBuffCoroutine);
            }
            
            var player = CharacterMainControl.Main;
            if (player != null && player.CharacterItem != null)
            {
                RemoveSelfBuff(player.CharacterItem, false); // false = Mod关闭时不安静移除
            }
            
            // 清理 Buff 模板
            if (earthSeekerBuffTemplate != null) 
            {
                UnityEngine.Object.Destroy(earthSeekerBuffTemplate.gameObject);
                WriteToLog("EarthSeeker Buff 模板已销毁。");
            }
            
            WriteToLog("WeaponSkillManager.OnBeforeDeactivate() 运行。系统已关闭。");
        }

        #endregion

        #region Skill System Logic

        private IEnumerator SkillCheckLoop()
        {
            while (true)
            {
                yield return null;
                try
                {
                    if (Input.GetKeyDown(KeyCode.Q))
                    {
                        WriteToLog("检测到 Q 键按下！");
                        TryPerformWeaponSkill();
                    }
                }
                catch (Exception ex)
                {
                    WriteToLog($"!! 严重错误 !! SkillCheckLoop 协程崩溃: {ex.Message}");
                    Debug.LogError("[DuckSoulsDLC] 技能检测循环出错: " + ex.ToString());
                }
            }
        }

        private void TryPerformWeaponSkill()
        {
            WriteToLog("TryPerformWeaponSkill() (EarthSeeker) 已调用。");

            var player = CharacterMainControl.Main;
            if (player == null)
            {
                WriteToLog("检查失败：player 为 null。");
                return;
            }

            player.CurrentAction?.StopAction();

            Item equippedMeleeWeapon = null;
            try
            {
                equippedMeleeWeapon = player.MeleeWeaponSlot()?.Content;
                WriteToLog($"获取近战槽武器：{(equippedMeleeWeapon != null ? equippedMeleeWeapon.TypeID.ToString() : "NULL")}");
            }
            catch (Exception ex)
            {
                WriteToLog($"!! 严重错误 !! 获取 player.MeleeWeaponSlot() 失败: {ex.Message}");
                return;
            }

            // 1. 检查武器
            if (equippedMeleeWeapon == null || equippedMeleeWeapon.TypeID != WEAPON_ID_EARTHSEEKER)
            {
                WriteToLog($"检查失败：没有 *装备* EarthSeeker 武器 (需要 {WEAPON_ID_EARTHSEEKER}，实际为 {(equippedMeleeWeapon != null ? equippedMeleeWeapon.TypeID.ToString() : "NULL")})。");
                return;
            }

            // 2. 检查精力
            if (player.CurrentStamina < STAMINA_COST_EARTHSEEKER)
            {
                WriteToLog($"检查失败：精力不足 (需要 {STAMINA_COST_EARTHSEEKER}，实际为 {player.CurrentStamina})。");
                DialogueBubblesManager.Show("...精力不足...", player.transform, duration: 2f).Forget();
                return;
            }

            // 3. 消耗精力并执行
            player.UseStamina(STAMINA_COST_EARTHSEEKER);
            DialogueBubblesManager.Show("嘎！！！！", player.transform, duration: 3f).Forget();
            WriteToLog("检查通过！准备启动 AOE 协程和自我 Buff 协程...");

            // 启动敌人 AOE 索敌
            _modInstance.StartCoroutine(AoeSlowAndDamageEffect(player.transform.position, player.gameObject));

            // 启动/刷新玩家自我 Buff
            if (_selfBuffCoroutine != null)
            {
                _modInstance.StopCoroutine(_selfBuffCoroutine);
            }
            _selfBuffCoroutine = _modInstance.StartCoroutine(ApplySelfBuffEffect(player, SELF_BUFF_DURATION_EARTHSEEKER));
        }

        /// <summary>
        /// 扫描敌人 (EarthSeeker)
        /// </summary>
        private IEnumerator AoeSlowAndDamageEffect(Vector3 origin, GameObject playerObject)
        {
            WriteToLog($"AoeSlowAndDamageEffect: 正在扫描区域 (半径 {SKILL_RADIUS_EARTHSEEKER}m)...");

            int numHits = Physics.OverlapSphereNonAlloc(
                origin, 
                SKILL_RADIUS_EARTHSEEKER, // 15f
                _hitsBuffer, 
                Physics.AllLayers, 
                QueryTriggerInteraction.Ignore
            );

            WriteToLog($"扫描完毕：在 {SKILL_RADIUS_EARTHSEEKER} 米内找到 {numHits} 个碰撞体 (已扫描所有图层)。");

            HashSet<Health> enemiesHit = new HashSet<Health>();
            int enemiesAffected = 0;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("--- 开始详细扫描 (最终修复版 v2 - 层级修复) ---");

            for (int i = 0; i < numHits; i++)
            {
                Collider hit = _hitsBuffer[i];
                
                if (hit.gameObject == playerObject || hit.transform.IsChildOf(playerObject.transform))
                {
                    continue;
                }

                Health targetHealth = hit.GetComponentInParent<Health>();
                
                if (targetHealth == null || targetHealth.IsDead || !enemiesHit.Add(targetHealth))
                {
                    continue;
                }
                
                AICharacterController enemyAI = targetHealth.gameObject.GetComponentInChildren<AICharacterController>();
                Item targetStats = targetHealth.gameObject.GetComponentInChildren<Item>(); 
                
                sb.AppendLine($"  - [物体: {hit.gameObject.name}，图层: {LayerMask.LayerToName(hit.gameObject.layer)}]");
                sb.AppendLine($"    - 向上找到 Health? : 是 ({targetHealth.gameObject.name})");
                sb.AppendLine($"    - 向下/子物体中找到 AI? : {(enemyAI != null ? $"是 ({enemyAI.gameObject.name})" : "否")}");
                sb.AppendLine($"    - 向下/子物体中找到 Item(Stats)? : {(targetStats != null ? $"是 ({targetStats.gameObject.name})" : "否")}");

                if (enemyAI != null && targetStats != null)
                {
                    sb.AppendLine($"    -> [!!] 判定为有效敌人: {targetHealth.gameObject.name} [!!]");
                    WriteToLog($"找到有效敌人: {targetHealth.gameObject.name}。准备为其启动 ApplyEffectToTarget 协程。");
                    
                    _modInstance.StartCoroutine(ApplyEffectToTarget(targetStats, targetHealth, SKILL_DURATION_EARTHSEEKER));
                    
                    enemiesAffected++;
                }
                else
                {
                     sb.AppendLine($"    -> [X] 判定无效：缺少 AI 或 Item(Stats) 组件。");
                }
            }

            sb.AppendLine("--- 详细扫描结束 ---");
            WriteToLog(sb.ToString()); 
            WriteToLog($"索敌结束：共找到 {enemiesAffected} 个【新】的有效敌人。");

            Array.Clear(_hitsBuffer, 0, numHits);
            
            yield return null;
        }


        /// <summary>
        /// 对单个敌人施加效果 (EarthSeeker)
        /// (包含 0 血不死修复)
        /// </summary>
        private IEnumerator ApplyEffectToTarget(Item targetStats, Health targetHealth, float duration)
        {
            if (targetStats == null || targetHealth == null || targetHealth.IsDead)
            {
                WriteToLog($"ApplyEffect: 目标 {targetStats?.gameObject.name ?? "NULL"} 在协程启动时就无效了。");
                yield break;
            }

            object slowSource = new object();
            string targetName = targetHealth.gameObject.name; 

            // 1. 施加减速 (50% 减速)
            try
            {
                targetStats.GetStat(STAT_WALK_SPEED.GetHashCode())?.AddModifier(new Modifier(ModifierType.PercentageAdd, SPEED_PENALTY_EARTHSEEKER, slowSource));
                targetStats.GetStat(STAT_RUN_SPEED.GetHashCode())?.AddModifier(new Modifier(ModifierType.PercentageAdd, SPEED_PENALTY_EARTHSEEKER, slowSource));
                WriteToLog($"ApplyEffect: 已对 {targetName} 施加减速。");
            }
            catch (Exception ex)
            {
                WriteToLog($"!! 严重错误 !! [目标: {targetName}] 施加 '群体减速' 属性时出错: {ex.ToString()}");
            }

            // 2. 持续造成伤害 (3/s)
            float elapsedTime = 0f;
            
            while (elapsedTime < duration) // 10 秒
            {
                if (targetHealth == null || targetHealth.IsDead)
                {
                    WriteToLog($"ApplyEffect: 目标 {targetName} 在效果结束前死亡或消失。");
                    break;
                }

                float damageToApply = DAMAGE_PER_SEC_EARTHSEEKER * Time.deltaTime;
                
                if (targetHealth.CurrentHealth > 1f)
                {
                    targetHealth.AddHealth(-damageToApply);
                    if (targetHealth.CurrentHealth < 1f)
                    {
                        targetHealth.CurrentHealth = 1f;
                    }
                }

                elapsedTime += Time.deltaTime;
                yield return null;
            }

            WriteToLog($"ApplyEffect: {targetName} 的 {duration} 秒效果已结束。");

            // 3. 移除减速
            try
            {
                if (targetStats != null)
                {
                    targetStats.GetStat(STAT_WALK_SPEED.GetHashCode())?.RemoveAllModifiersFromSource(slowSource);
                    targetStats.GetStat(STAT_RUN_SPEED.GetHashCode())?.RemoveAllModifiersFromSource(slowSource);
                    WriteToLog($"ApplyEffect: 已移除 {targetName} 的减速。");
                }
                else
                {
                     WriteToLog($"ApplyEffect: 目标 {targetName} (Stats: {targetStats.gameObject.name}) 在移除减速前已消失，无需移除。");
                }
            }
            catch (Exception ex) 
            { 
                WriteToLog($"!! 严重错误 !! [目标: {targetName}] 移除 '群体减速' 属性时出错: {ex.ToString()}");
            }
        }

        #endregion

        // ===================================================================
        // [!!               玩家自我 Buff 逻辑 (EarthSeeker)         !!]
        // ===================================================================

        /// <summary>
        /// [!! 已更新 !!] 施加属性 + UI Buff
        /// </summary>
        private IEnumerator ApplySelfBuffEffect(CharacterMainControl player, float duration)
        {
            WriteToLog("ApplySelfBuffEffect: 启动自我 Buff...");
            Item playerItem = player?.CharacterItem;
            
            // 获取 Buff 管理器
            if (buffManager == null) buffManager = player?.GetBuffManager();
            
            if (playerItem == null || buffManager == null)
            {
                WriteToLog($"ApplySelfBuffEffect: 失败，playerItem({playerItem != null}) 或 buffManager({buffManager != null}) 为 null。");
                yield break;
            }

            // 1. 移除旧 Buff (属性 + UI)
            RemoveSelfBuff(playerItem, true); // true = 安静地移除旧的

            // 2. 施加新属性 Buff
            try
            {
                playerItem.GetStat(STAT_WALK_SPEED.GetHashCode())?.AddModifier(new Modifier(ModifierType.PercentageAdd, SELF_BUFF_SPEED_BONUS_EARTHSEEKER, selfBuffSource));
                playerItem.GetStat(STAT_RUN_SPEED.GetHashCode())?.AddModifier(new Modifier(ModifierType.PercentageAdd, SELF_BUFF_SPEED_BONUS_EARTHSEEKER, selfBuffSource));
                playerItem.GetStat(STAT_MELEE_DAMAGE.GetHashCode())?.AddModifier(new Modifier(ModifierType.Add, SELF_BUFF_DAMAGE_BONUS_EARTHSEEKER, selfBuffSource));
                WriteToLog($"ApplySelfBuffEffect: 已施加 {SELF_BUFF_SPEED_BONUS_EARTHSEEKER * 100}% 移速和 {SELF_BUFF_DAMAGE_BONUS_EARTHSEEKER * 100}% 近战伤害，持续 {duration} 秒。");
            }
            catch (Exception ex)
            {
                WriteToLog($"!! 严重错误 !! 施加 '自我 Buff' 属性时出错: {ex.ToString()}");
            }

            // 3. 施加新 UI Buff
            try
            {
                Buff template = GetEarthSeekerBuffTemplate();
                if (template != null)
                {
                    // 设置持续时间并添加
                    typeof(Buff).GetField("totalLifeTime", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(template, duration);
                    buffManager.AddBuff(template, player, 0);
                    WriteToLog("ApplySelfBuffEffect: 已施加 '战鸭咆哮' UI Buff。");
                }
            }
            catch (Exception ex) { WriteToLog($"!! 严重错误 !! 施加 '战鸭咆哮' UI Buff 时出错: {ex.ToString()}"); }


            // 4. 等待持续时间
            yield return new WaitForSeconds(duration);

            // 5. 移除 Buff (属性 + UI)
            WriteToLog($"ApplySelfBuffEffect: 自我 Buff 时间到 (来自 {duration} 秒前的协程)。");
            RemoveSelfBuff(playerItem, false); // false = 显示移除提示
            _selfBuffCoroutine = null; // 清理协程引用
        }

        /// <summary>
        /// [!! 已更新 !!] 移除属性 + UI Buff
        /// </summary>
        private void RemoveSelfBuff(Item playerItem, bool quiet = false)
        {
            // 1. 移除属性
            if (playerItem != null)
            {
                try
                {
                    playerItem.GetStat(STAT_WALK_SPEED.GetHashCode())?.RemoveAllModifiersFromSource(selfBuffSource);
                    playerItem.GetStat(STAT_RUN_SPEED.GetHashCode())?.RemoveAllModifiersFromSource(selfBuffSource);
                    playerItem.GetStat(STAT_MELEE_DAMAGE.GetHashCode())?.RemoveAllModifiersFromSource(selfBuffSource);
                    if (!quiet) WriteToLog("RemoveSelfBuff: 已移除自我 Buff 属性。");
                }
                catch (Exception ex)
                {
                    WriteToLog($"!! 严重错误 !! 移除 '自我 Buff' 属性时出错: {ex.ToString()}");
                }
            }
            
            // 2. 移除 UI Buff
            try
            {
                if (buffManager != null && earthSeekerBuffTemplate != null)
                {
                    buffManager.RemoveBuff(earthSeekerBuffTemplate.ID, false);
                    if (!quiet) WriteToLog("RemoveSelfBuff: 已移除 '战鸭咆哮' UI Buff。");
                }
            }
            catch (Exception ex) { WriteToLog($"!! 严重错误 !! 移除 '战鸭咆哮' UI Buff 时出错: {ex.ToString()}"); }
        }


        // ===================================================================
        // [!!               UI Buff 辅助方法             !!]
        // ===================================================================
        
        /// <summary>
        /// [新增] 加载所有 Buff 图标
        /// </summary>
        private void LoadBuffIcons()
        {
            try
            {
                string assemblyLocation = Assembly.GetExecutingAssembly().Location;
                string modDirectory = Path.GetDirectoryName(assemblyLocation);
                string iconsDirectory = Path.Combine(modDirectory, "icons");

                if (!Directory.Exists(iconsDirectory))
                {
                    WriteToLog($"[警告] 图标文件夹未找到: {iconsDirectory}");
                    return;
                }

                earthSeekerBuffIcon = LoadIconFromFile(iconsDirectory, ICON_NAME_EARTHSEEKER);
            }
            catch (Exception ex)
            {
                WriteToLog($"!! 严重错误 !! 加载图标时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// [新增] 从文件加载单个图标
        /// </summary>
        private Sprite LoadIconFromFile(string iconsDirectory, string fileName)
        {
            string iconPath = Path.Combine(iconsDirectory, fileName);
            if (!File.Exists(iconPath))
            {
                WriteToLog($"[警告] 图标文件未找到: {fileName}");
                return null;
            }

            try
            {
                byte[] fileData = File.ReadAllBytes(iconPath);
                Texture2D tex = new Texture2D(2, 2);
                if (tex.LoadImage(fileData))
                {
                    Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                    WriteToLog($"成功加载图标: {fileName}");
                    return sprite;
                }
            }
            catch (Exception ex) { WriteToLog($"!! 严重错误 !! 加载图标 {fileName} 失败: {ex.Message}"); }
            return null;
        }
        
        /// <summary>
        /// [新增] 获取“战鸭咆哮”的 Buff 模板
        /// </summary>
        private Buff GetEarthSeekerBuffTemplate()
        {
            if (earthSeekerBuffTemplate != null) return earthSeekerBuffTemplate;
            if (buffManager == null) buffManager = CharacterMainControl.Main?.GetBuffManager();
            if (buffManager == null)
            {
                WriteToLog("[警告] GetEarthSeekerBuffTemplate 无法获取 BuffManager。");
                return null;
            }

            // 如果图标加载失败，创建一个黄色的假图标
            Sprite iconToUse = earthSeekerBuffIcon ?? CreateDummySprite(Color.yellow);
            
            earthSeekerBuffTemplate = CreateBuffTemplate(
                "战鸭咆哮", 
                "移速提升10%，近战伤害提升10%。", 
                iconToUse, 
                BUFF_ID_EARTHSEEKER
            );
            return earthSeekerBuffTemplate;
        }
        
        /// <summary>
        /// [新增] 创建 Buff 模板 (来自 SpellManager)
        /// </summary>
        private Buff CreateBuffTemplate(string nameKey, string descriptionKey, Sprite icon, int id)
        {
            GameObject gameObject = new GameObject($"ModBuff_WSM_{id}"); // 添加WSM前缀以防冲突
            gameObject.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            Buff buff = gameObject.AddComponent<Buff>();

            Type typeFromHandle = typeof(Buff);

            try { typeFromHandle.GetField("id", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(buff, id); } catch { }
            try { typeFromHandle.GetField("displayName", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(buff, nameKey); } catch { }
            try { typeFromHandle.GetField("description", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(buff, descriptionKey); } catch { }
            try { typeFromHandle.GetField("icon", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(buff, icon); } catch { }
            try { typeFromHandle.GetField("limitedLifeTime", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(buff, true); } catch { }

            WriteToLog($"Buff 模板已创建: {nameKey} (ID: {id})");
            return buff;
        }

        /// <summary>
        /// [新增] 创建一个假的图标 (来自 SpellManager)
        /// </summary>
        private Sprite CreateDummySprite(Color color)
        {
            WriteToLog("[警告] 正在创建假的 Buff 图标...");
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

        // --- 日志写入方法 ---
        private void WriteToLog(string message)
        {
            if (logFilePath == null)
            {
                Debug.Log($"[DuckSoulsDLC-LOG] (文件未初始化): {message}");
                return;
            }
            
            try
            {
                File.AppendAllText(logFilePath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
                Debug.Log($"[DuckSoulsDLC-LOG] {message}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DuckSoulsDLC] 写入日志文件失败: {ex.Message}");
            }
        }
    }
}