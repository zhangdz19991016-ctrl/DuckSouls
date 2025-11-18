﻿using System;
using System.Collections;
using System.IO; 
using System.Reflection; 
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement; 

namespace PileErico
{
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        // --- 1. 管理器实例 ---
        private LootManager? lootManager;
        private EstusFlaskManager? estusFlaskManager;
        private ShopManager? shopManager;
        private BossHealthHUDManager? bossHudManager;

        // --- 2. 通用 ---
        public static bool isActivated = false; 
        
        // [新] 日志文件路径
        public static string? logPath;


        // --- 3. Mod 生命周期 (已重构) ---

        protected override void OnAfterSetup()
        {
            base.OnAfterSetup();
            base.StartCoroutine(this.DelayedInitialization());
        }

        protected override void OnBeforeDeactivate()
        {
            if (!isActivated) return; 
            isActivated = false;
            
            LogToFile("[PileErico] 模组开始停用...");

            try { this.lootManager?.Deactivate(); }
            catch (Exception ex) { LogErrorToFile($"[PileErico] 停用 LootManager 失败: {ex.Message}"); }
            
            try { this.shopManager?.Deactivate(); }
            catch (Exception ex) { LogErrorToFile($"[PileErico] 停用 ShopManager 失败: {ex.Message}"); }

            try { this.estusFlaskManager?.Deactivate(); }
            catch (Exception ex) { LogErrorToFile($"[PileErico] 停用 EstusFlaskManager 失败: {ex.Message}"); }

            try { this.bossHudManager?.Deactivate(); }
            catch (Exception ex) { LogErrorToFile($"[PileErico] 停用 BossHealthHUDManager 失败: {ex.Message}"); }

            LogToFile("[PileErico] 模组已停用。");
        }

        private IEnumerator DelayedInitialization()
        {
            yield return new WaitForSeconds(0.1f);
            if (isActivated) yield break;
            isActivated = true;
            
            string configDir = string.Empty;

            // --- 初始化功能 ---
            if (Application.isPlaying)
            {
                // 创建配置文件夹
                try
                {
                    configDir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty, "Configs");
                    if (!Directory.Exists(configDir))
                    {
                        Directory.CreateDirectory(configDir);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PileErico] 创建 Configs 文件夹失败: {ex.Message}");
                }

                // [新] 初始化文件日志
                try
                {
                    // configDir 是 "[ModPath]/Configs", 我们要 "[ModPath]/logs"
                    string logDir = Path.Combine(Path.GetDirectoryName(configDir) ?? string.Empty, "logs");
                    if (!Directory.Exists(logDir))
                    {
                        Directory.CreateDirectory(logDir);
                    }
                    logPath = Path.Combine(logDir, "PileErico.log");
                    File.WriteAllText(logPath, $"--- [PileErico] 日志开始于 {DateTime.Now} ---\n");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PileErico] 创建日志文件失败: {ex.Message}");
                    logPath = null; // 如果失败，则不写入文件
                }

                LogToFile("[PileErico] (全部功能) 开始初始化...");
                
                // 1. 初始化 Boss 血条 HUD
                try
                {
                    GameObject hudRoot = new GameObject("BossHealthHUDRoot");
                    UnityEngine.Object.DontDestroyOnLoad(hudRoot);
                    this.bossHudManager = hudRoot.AddComponent<BossHealthHUDManager>();
                    LogToFile("[PileErico] BossHealthHUDManager 初始化完成。");
                }
                catch (Exception ex)
                {
                    LogErrorToFile($"[PileErico] 初始化 BossHealthHUDManager 失败: {ex.Message}");
                }
                
                // 2. 初始化战利品功能
                this.lootManager = new LootManager(this, configDir, this.bossHudManager); 
                this.lootManager.Initialize(); 
                
                // 3. 初始化商店功能
                this.shopManager = new ShopManager(this, configDir);
                this.shopManager.Initialize(); 

                // 4. 初始化原素瓶功能
                this.estusFlaskManager = new EstusFlaskManager(this);
                this.estusFlaskManager.Initialize(); 

                ModBehaviour.LogToFile("[PileErico] (全部功能) 初始化完成。");
            }
        }

        // --- 4. 日志 [修改] ---
        #region Log Functions
        
        public static void LogToFile(string message)
        {
            string logMessage = $"[INFO] {DateTime.Now:T}: {message}";
            Debug.Log("[PileErico]" + logMessage);
            TryWriteToLogFile(logMessage);
        }

        public static void LogWarningToFile(string message)
        {
            string logMessage = $"[WARN] {DateTime.Now:T}: {message}";
            Debug.LogWarning("[PileErico]" + logMessage);
            TryWriteToLogFile(logMessage);
        }

        public static void LogErrorToFile(string message)
        {
            string logMessage = $"[ERROR] {DateTime.Now:T}: {message}";
            Debug.LogError("[PileErico]" + logMessage);
            TryWriteToLogFile(logMessage);
        }
        
        // [新] 写入文件的辅助方法
        private static void TryWriteToLogFile(string message)
        {
            if (logPath == null) return;
            try
            {
                File.AppendAllText(logPath, message + "\n");
            }
            catch (Exception ex)
            {
                // 写入日志失败，在控制台报告一次，然后禁用文件日志
                Debug.LogError($"[PileErico] 写入日志文件失败: {ex.Message}");
                logPath = null; 
            }
        }
        
        #endregion
    }
}