using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Duckov.Economy;
using Duckov.Economy.UI; // [Gemini 修正] 添加缺失的 using
using Duckov.Scenes;
using Duckov.Utilities;
using ItemStatsSystem; // [Gemini 修正] 添加
using ItemStatsSystem.Items; // [Gemini] 添加
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PileErico
{
    public class ShopManager
    {
        // 商店功能
        private ShopConfig shopConfig = null!;
        
        // [Gemini] 引用主模组，用于启动协程
        private readonly ModBehaviour modBehaviour;
        private readonly string configDir;

        public ShopManager(ModBehaviour modBehaviour, string configDir)
        {
            this.modBehaviour = modBehaviour;
            this.configDir = configDir;
        }

        /// <summary>
        /// 由 ModBehaviour 调用以启动此功能
        /// </summary>
        public void Initialize()
        {
            ModBehaviour.LogToFile("[ShopManager] 正在初始化...");
            // 1. 加载商店配置
            this.LoadShopConfig(configDir);
            
            // 2. 挂载场景加载事件
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        /// <summary>
        /// 由 ModBehaviour 调用以停用此功能
        /// </summary>
        public void Deactivate()
        {
            ModBehaviour.LogToFile("[ShopManager] 正在停用...");
            // 注销场景事件
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }


        // --- 自定义商人 (保持不变) ---
        #region New Custom Merchant Functions
        
        // [Gemini] LoadShopConfig 保持不变
        private void LoadShopConfig(string configDir)
        {
            string path = Path.Combine(configDir, "ShopConfig.json");
            ModBehaviour.LogToFile("[PileErico] 尝试加载商店配置文件: " + path);
            
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    this.shopConfig = JsonConvert.DeserializeObject<ShopConfig>(json)!;
                    if (this.shopConfig == null)
                    {
                        ModBehaviour.LogWarningToFile("[PileErico] 商店配置文件 ShopConfig.json 解析失败。");
                        this.shopConfig = new ShopConfig(); // 创建一个空配置以避免 NullReference
                    }
                }
                catch (Exception ex)
                {
                    ModBehaviour.LogErrorToFile("[PileErico] 加载商店配置时发生错误: " + ex.Message);
                    this.shopConfig = new ShopConfig(); // 出错时创建一个空配置
                }
            }
            else
            {
                ModBehaviour.LogToFile("[PileErico] 商店配置文件 ShopConfig.json 未找到。将创建默认配置。");
                this.shopConfig = new ShopConfig();
                this.shopConfig.ItemsToSell.Add(new ShopItemEntry { ItemID = 9001, MaxStock = 10, PriceFactor = 1.0f, Possibility = 1.0f });
                
                string text = JsonConvert.SerializeObject(this.shopConfig, Formatting.Indented);
                try
                {
                    File.WriteAllText(path, text);
                    ModBehaviour.LogToFile("[PileErico] 已创建默认的 ShopConfig.json。");
                }
                catch (Exception ex2)
                {
                    ModBehaviour.LogErrorToFile("[PileErico] 创建默认 ShopConfig.json 时发生错误: " + ex2.Message);
                }
            }
        }
        
        // [Gemini] OnSceneLoaded 保持不变
        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "Base_SceneV2")
            {
                if (this.shopConfig == null)
                {
                    ModBehaviour.LogWarningToFile("[PileErico] 商店配置未加载，无法生成商人。");
                    return;
                }
                ModBehaviour.LogToFile("[PileErico] 正在基地生成(克隆)自定义商人...");
                // [Gemini] 使用 modBehaviour 启动协程
                modBehaviour.StartCoroutine(SetupClonedMerchant());
            }
        }

        // [Gemini] SetupClonedMerchant 保持不变
        private IEnumerator SetupClonedMerchant()
        {
            // 延迟1秒，确保场景完全加载
            yield return new WaitForSeconds(1f);

            ModBehaviour.LogToFile("[PileErico] 启动克隆商人设置协程...");

            try
            {
                var originalSaleMachine = GameObject.Find("Buildings/SaleMachine");
                if (originalSaleMachine == null)
                {
                    ModBehaviour.LogErrorToFile("[PileErico] 克隆失败：未找到 'Buildings/SaleMachine'！");
                    yield break;
                }

                ModBehaviour.LogToFile("[PileErico] 找到 'Buildings/SaleMachine'，开始克隆...");
                // [Gemini] 使用 GameObject.Instantiate
                var myMerchantClone = GameObject.Instantiate(originalSaleMachine);
                myMerchantClone.name = "PileErico_Cloned_Merchant"; // 设置一个唯一的名字
                
                // 设置父对象，与原版售货机同级
                myMerchantClone.transform.SetParent(originalSaleMachine.transform.parent, true);

                // 使用 config 中的位置
                Vector3 targetPosition = shopConfig.GetPosition();
                myMerchantClone.transform.position = targetPosition;
                
                // [Gemini 修正] 隐藏售货机自己的模型
                foreach (Transform child in myMerchantClone.transform)
                {
                    if (child.name == "Visual")
                    {
                        child.gameObject.SetActive(false);
                        ModBehaviour.LogToFile("[PileErico] 已隐藏售货机 'Visual' 模型。");
                    }
                }

                // [Gemini 修正] 异步加载商人模型并附加
                LoadMerchantModel(myMerchantClone.transform).Forget();

                // 售货机预设体上的 StockShop 组件在子对象 "PerkWeaponShop" 上
                var stockShopTransform = myMerchantClone.transform.Find("PerkWeaponShop");
                if (stockShopTransform == null)
                {
                    ModBehaviour.LogErrorToFile("[PileErico] 克隆失败：未在克隆体上找到 'PerkWeaponShop' 子对象！");
                    GameObject.Destroy(myMerchantClone); // 清理失败的克隆体
                    yield break;
                }

                var stockShop = stockShopTransform.GetComponent<StockShop>();
                if (stockShop == null)
                {
                    ModBehaviour.LogErrorToFile("[PileErico] 克隆失败：'PerkWeaponShop' 上没有 StockShop 组件！");
                    GameObject.Destroy(myMerchantClone);
                    yield break;
                }

                // 关键步骤：配置这个克隆的商店 (使用您原有的物品填充逻辑)
                ConfigureClonedShop(stockShop);

                // 激活商人
                myMerchantClone.SetActive(true);
                ModBehaviour.LogToFile($"[PileErico] 克隆商人已在 {targetPosition} 位置创建成功。");
            }
            catch (Exception e)
            {
                ModBehaviour.LogErrorToFile($"[PileErico] 设置克隆商人时发生严重异常: {e.Message}\n{e.StackTrace}");
            }
        }

        // [Gemini 修正] LoadMerchantModel 已更新，用于销毁 "幽灵交互点"
        async UniTask LoadMerchantModel(Transform parentMachine)
        {
            try
            {
                CharacterRandomPreset? characterRandomPreset = GetCharacterPreset(shopConfig.MerchantPresetName);
                if (characterRandomPreset == null)
                {
                    ModBehaviour.LogErrorToFile($"[PileErico] 错误: 找不到NPC预设 {shopConfig.MerchantPresetName}");
                    return;
                }

                Vector3 position = shopConfig.GetPosition();
                Vector3 faceTo = shopConfig.GetFacing();
                
                ModBehaviour.LogToFile($"[PileErico] 正在加载商人模型: {shopConfig.MerchantPresetName}");

                var merchantCharacter = await characterRandomPreset!.CreateCharacterAsync(position, faceTo,
                    MultiSceneCore.MainScene!.Value.buildIndex, (CharacterSpawnerGroup)null!, false);
                
                if (merchantCharacter == null)
                {
                    ModBehaviour.LogErrorToFile("[PileErico] 创建商人角色实例失败。");
                    return;
                }
                
                ModBehaviour.LogToFile("[PileErico] 商人模型加载成功，正在附加到售货机...");
                
                // 1. 设置父对象和位置
                merchantCharacter.transform.SetParent(parentMachine, true);
                merchantCharacter.transform.localPosition = Vector3.zero; // 相对父对象（售货机）居中
                merchantCharacter.transform.rotation = Quaternion.LookRotation(faceTo - position); // 设置朝向

                // 2. 剥离所有不需要的组件，只保留模型
                ModBehaviour.LogToFile("[PileErico] 正在剥离商人模型组件...");
                
                // 移除AI (AI在子对象上)
                var aiChild = merchantCharacter.transform.Find(shopConfig.MerchantPresetName.Replace("EnemyPreset_", "AIController_") + "(Clone)");
                if (aiChild != null)
                {
                    GameObject.Destroy(aiChild.gameObject); // [Gemini] 使用 GameObject.Destroy
                    ModBehaviour.LogToFile("[PileErico] 已移除 AI Controller 子对象。");
                }
                
                // [Gemini] 使用 GameObject.Destroy
                // 移除根组件
                if (merchantCharacter.GetComponent<CharacterController>() != null)
                    GameObject.Destroy(merchantCharacter.GetComponent<CharacterController>());
                
                if (merchantCharacter.GetComponent<Health>() != null)
                    GameObject.Destroy(merchantCharacter.GetComponent<Health>());
                    
                if (merchantCharacter.GetComponent<Movement>() != null)
                    GameObject.Destroy(merchantCharacter.GetComponent<Movement>());
                
                if (merchantCharacter.GetComponent<CharacterItemControl>() != null)
                    GameObject.Destroy(merchantCharacter.GetComponent<CharacterItemControl>());

                // [Gemini 最终修正] 查找并移除商人自带的“特殊商人”子对象
                // (这将同时移除它自带的 InteractableBase 和 InteractMarker, 彻底解决“幽灵交互点”问题)
                var merchantChild = GetSpecialMerchantChild(merchantCharacter.transform);
                if (merchantChild != null)
                {
                    GameObject.Destroy(merchantChild.gameObject); // [Gemini] 使用 GameObject.Destroy
                    ModBehaviour.LogToFile("[PileErico] 已移除商人模型的 'SpecialAttachment_Merchant_' 子对象 (幽灵交互点)。");
                }
                else
                {
                    ModBehaviour.LogWarningToFile("[PileErico] 未能找到 'SpecialAttachment_Merchant_' 子对象来移除幽灵交互点。");
                }
                
                // 移除所有碰撞体 (以防万一)
                foreach (var col in merchantCharacter.GetComponentsInChildren<Collider>())
                {
                    GameObject.Destroy(col); // [Gemini] 使用 GameObject.Destroy
                }
                ModBehaviour.LogToFile("[PileErico] 商人模型剥离完成。");
            }
            catch (Exception ex)
            {
                ModBehaviour.LogErrorToFile($"[PileErico] 加载商人模型时出错: {ex.Message}");
            }
        }


        // [Gemini] ConfigureClonedShop 保持不变 (已修复物品加载)
        private void ConfigureClonedShop(StockShop stockShop)
        {
            // 1. 设置一个唯一的 merchantID (借鉴 SuperPerkShop)
            try
            {
                var merchantIDField = typeof(StockShop).GetField("merchantID", 
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (merchantIDField != null)
                {
                    // 使用一个您模组专属的ID
                    merchantIDField.SetValue(stockShop, "PileErico_Merchant_ID");
                    ModBehaviour.LogToFile("[PileErico] 克隆商人的 MerchantID 已设置为: PileErico_Merchant_ID");
                }
            }
            catch (Exception ex)
            {
                 ModBehaviour.LogWarningToFile($"[PileErico] 反射设置 merchantID 失败: {ex.Message}");
            }
            
            // 2. 清空克隆体原有的物品
            stockShop.entries.Clear(); 

            // 3. 从 shopConfig.ItemsToSell 填充商店 (您原有的逻辑)
            if (shopConfig.ItemsToSell == null || shopConfig.ItemsToSell.Count == 0)
            {
                ModBehaviour.LogToFile("[PileErico] 商店配置中没有物品，商人将不售卖任何东西。");
            }
            else
            {
                if (ItemAssetsCollection.Instance == null)
                {
                     ModBehaviour.LogErrorToFile("[PileErico] ItemAssetsCollection.Instance 为空！无法添加物品。");
                     return;
                }

                ModBehaviour.LogToFile($"[PileErico] 正在从 config 加载 {shopConfig.ItemsToSell.Count} 种物品...");
                foreach (var itemEntry in shopConfig.ItemsToSell)
                {
                    // [Gemini 修正] 移除 ItemAssetsCollection 的检查 (CS1061 错误)
                    // 恢复您原始的逻辑：直接添加
                    var newShopItem = new StockShopDatabase.ItemEntry
                    {
                        typeID = itemEntry.ItemID,
                        maxStock = itemEntry.MaxStock,
                        priceFactor = itemEntry.PriceFactor,
                        possibility = itemEntry.Possibility,
                        forceUnlock = true 
                    };
                    stockShop.entries.Add(new StockShop.Entry(newShopItem));
                }
            }
            ModBehaviour.LogToFile($"[PileErico] 成功为克隆商人应用 {stockShop.entries.Count} 种自定义物品。");

            // 4. 刷新商店 (调用您已有的 RefreshShop 方法)
            // [Gemini] RefreshShop 已移至此类中
            RefreshShop(stockShop);
        }
            
        // --- 辅助方法 (用于商店) ---
        
        // [Gemini 修正] 重新添加 GetCharacterPreset (加载模型需要它)
        CharacterRandomPreset? GetCharacterPreset(string characterPresetName)
        {
            foreach (var characterRandomPreset in GameplayDataSettings.CharacterRandomPresetData.presets)
            {
                if (characterPresetName == characterRandomPreset.name)
                {
                    return characterRandomPreset;
                }
            }
            ModBehaviour.LogWarningToFile($"[PileErico] 在 GameplayDataSettings 中未找到预设: {characterPresetName}");
            return null;
        }

        // [Gemini 最终修正] 重新添加 GetSpecialMerchantChild (移除幽灵交互点需要它)
        Transform? GetSpecialMerchantChild(Transform parent)
        {
            foreach (Transform child in parent)
            {
                if (child.name.StartsWith("SpecialAttachment_Merchant_"))
                    return child;
            }
            return null;
        }

        // [Gemini] RefreshShop 保持不变, 但移入此类中并保持 static
        public static void RefreshShop(StockShop stockShop)
        {
            if (stockShop == null)
            {
                ModBehaviour.LogToFile("[PileErico] RefreshShop 失败: stockShop == null");
                return;
            }
            
            var refreshMethod = typeof(StockShop).GetMethod("DoRefreshStock",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (refreshMethod == null)
            {
                ModBehaviour.LogWarningToFile("[PileErico] 未找到 DoRefreshStock 方法");
                return;
            }
            try { refreshMethod.Invoke(stockShop, null); }
            catch (Exception ex) { ModBehaviour.LogErrorToFile($"[PileErico] 调用 DoRefreshStock 异常: {ex.Message}"); }

            var lastTimeField = typeof(StockShop).GetField("lastTimeRefreshedStock",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (lastTimeField == null)
            {
                ModBehaviour.LogWarningToFile("[PileErico] 未找到 lastTimeRefreshedStock 字段");
                return;
            }
            // [Gemini 最终修正] CS0103: ModErrorToFile -> ModBehaviour.LogErrorToFile
            try { lastTimeField.SetValue(stockShop, DateTime.UtcNow.ToBinary()); }
            catch (Exception ex) { ModBehaviour.LogErrorToFile($"[PileErico] 设置 lastTimeRefreshedStock 异常: {ex.Message}"); }
        }
        
        #endregion
    }
}