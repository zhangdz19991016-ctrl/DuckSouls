// 文件名: BossHealthHUDManager.cs
// [最终版 v9 - 全宽横幅]
// - 修复了 Image.Sprite 为 null 导致 fillAmount 失效的问题
// - 升级为艾尔登法环风格的虚血条 (0.5秒延迟)
// - 升级为“纯名单”Boss识别
// - DUCK HUNTED 动画使用 Coroutine (协程) (汇聚1.0s, 保持0.5s, 淡出1.0s)
// - 修复了 CS1503 编译错误 (transform -> gameObject)
// - 修复了 CS8625, CS8618, CS0414 三个编译警告
// - [新] DUCK HUNTED 横幅背景现在会水平拉伸以填满屏幕
using System;
using System.Collections; // [v6] 动画需要
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Duckov;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.UI;

namespace PileErico
{
    public class BossHealthHUDManager : MonoBehaviour
    {
        // ───── 核心逻辑 (无改动) ─────
        private CharacterMainControl? _player;
        private readonly List<CharacterMainControl> _discoveredBosses = new List<CharacterMainControl>();
        private readonly List<CharacterMainControl> _trackedBosses = new List<CharacterMainControl>();
        private bool _uiEnabled = true;

        private float _maxBossDisplayDistance = 20f;
        private readonly Dictionary<CharacterMainControl, float> _lastHpMap = new Dictionary<CharacterMainControl, float>();
        private readonly List<CharacterMainControl?> _cleanupList = new List<CharacterMainControl?>();

        private int _previousTrackedBossCount = -1;
        private int _previousDrawnBossCount = -1;

        // [v4] 纯名单模式 Boss 列表
        private readonly HashSet<string> _knownBossNames = new HashSet<string>
        {
            "光之男","矮鸭","牢登","急速团长","暴走街机","校霸","BA队长","炸弹狂人","三枪哥","喷子","矿长","高级工程师","蝇蝇队长","迷塞尔","维达","？？？","路障","啪啦啪啦","咕噜咕噜","噗咙噗咙","比利比利","口口口口",
            "Man of Light","Pato Chapo","Lordon","Speedy Group Commander","Vida","Big Xing","Rampaging Arcade","Senior Engineer","Triple-Shot Man","Misel","Mine Manager","Shotgunner","Mad Bomber","Security Captain","Fly Captain","School Bully","Billy Billy","Gulu Gulu","Pala Pala","Pulu Pulu","Koko Koko","Roadblock",
        };

        // ───── DUCK HUNTED ─────
        // [v7 修复 CS0414] 删除了无用的 _showDuckHunted 变量
        private string? _lastKilledBossName;
        // [v7 修复 CS8618] 协程动画控制器 (声明为可空)
        private Coroutine? _duckHuntedCoroutine;

        // ───── UGUI 元素引用 ─────
        private Canvas _canvas = null!;
        private List<HealthBarUI> _healthBarUIs = new List<HealthBarUI>();
        private const int MaxBossBars = 3;
        private GameObject _duckHuntedOverlay = null!;
        private CanvasGroup _duckHuntedCanvasGroup = null!;
        private Text _duckHuntedMainText = null!;
        private Text _duckHuntedSubText = null!;

        // [v6] "黑魂" 效果的重影文本
        private Text _duckHuntedGhostText1 = null!;
        private Text _duckHuntedGhostText2 = null!;
        
        // [v6] 缓存 RectTransforms 以提高动画性能
        private RectTransform _duckHuntedMainRect = null!;
        private RectTransform _duckHuntedGhost1Rect = null!;
        private RectTransform _duckHuntedGhost2Rect = null!;

        // [v7 修改] 动画计时 (总计 2.5s)
        private const float GhostConvergeTime = 1.0f; // 重影汇聚时间 (1.0秒)
        private const float GhostHoldTime = 0.5f;     // 汇聚后保持时间 (0.5秒)
        private const float FadeOutTime = 1.0f;     // 总淡出时间 (1.0秒)
        private const float GhostMaxOffset = 20f;     // 重影最大偏移 (20像素)

        // [修复] 静态 Sprite
        private static Sprite? _minimalSprite;

        // [v2] 虚血条 Class
        private class HealthBarUI
        {
            public GameObject Root = null!;
            public Image Fill = null!;
            public Image Fill_Ghost = null!;
            public Text NameText = null!;
            public Text HpText = null!;
            public Image BarBG = null!;
            public float CurrentGhostFill = 1.0f;
            public float LastKnownFill = 1.0f;
            public float GhostTimer = 0f;
            public const float GhostDelay = 0.5f;
            public const float GhostLerpSpeed = 2.0f;
        }

        // ───── 模组入口与生命周期 ─────

        private void Awake()
        {
            ModBehaviour.LogToFile("[BossHealthHUDManager] Awake");
            TryFindPlayer();
            CreateUGUI();
        }

        private void Update()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.F8))
            {
                _uiEnabled = !_uiEnabled;
                _canvas.gameObject.SetActive(_uiEnabled);
                ModBehaviour.LogToFile("[BossHealthHUDManager] HUD " + (_uiEnabled ? "ON" : "OFF"));
            }
            if (!_uiEnabled) return;
            if (_player == null) TryFindPlayer();

            UpdateBossDeathState();

            if (Time.frameCount % 15 == 0)
            {
                UpdateTrackedBosses();
            }

            // [v7 修复 CS0414] 已移除 Update 中的 DUCK HUNTED 动画逻辑

            // [v2] UGUI 更新常驻
            UpdateUGUI();
        }

        // [v4] 纯名单模式
        public void RegisterCharacter(CharacterMainControl character)
        {
            if (character == null || _discoveredBosses.Contains(character) || character == _player)
                return;

            string displayName = SafeGetName(character);
            ModBehaviour.LogToFile($"[BossHealthHUDManager] RegisterCharacter 被调用: {displayName} (Name: {character.name})");

            bool isKnownBoss = _knownBossNames.Contains(character.name) || _knownBossNames.Contains(displayName);

            if (isKnownBoss)
            {
                Health h = character.Health;
                if (h == null)
                {
                    ModBehaviour.LogToFile($"[BossHealthHUDManager] 忽略: '{displayName}' 在列表中，但 Health 组件为 null.");
                    return;
                }

                _discoveredBosses.Add(character);
                if (!_lastHpMap.ContainsKey(character))
                {
                    _lastHpMap[character] = h.CurrentHealth;
                }
                ModBehaviour.LogToFile($"[BossHealthHUDManager] 成功: '{displayName}' (HP: {h.MaxHealth}) 是一个已知的 Boss. 已添加.");
            }
            else
            {
                ModBehaviour.LogToFile($"[BossHealthHUDManager] 忽略: '{displayName}' (Name: {character.name}) 不在 _knownBossNames 列表中.");
            }
        }

        public void Deactivate()
        {
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
            _discoveredBosses.Clear();
            _trackedBosses.Clear();
            _lastHpMap.Clear();
        }

        // ───── 核心逻辑 (无改动) ─────

        private void UpdateTrackedBosses()
        {
            if (_player == null) return;

            List<CharacterMainControl> candidates = new List<CharacterMainControl>();

            foreach (CharacterMainControl boss in _discoveredBosses)
            {
                if (boss == null || !boss) continue;

                Health h = boss.Health;
                if (h == null || h.CurrentHealth <= 0f) continue;

                float dist = Vector3.Distance(_player.transform.position, boss.transform.position);
                if (dist > _maxBossDisplayDistance) continue;

                candidates.Add(boss);
            }

            candidates.Sort((a, b) =>
            {
                Health? ha = a != null ? a.Health : null;
                Health? hb = b != null ? b.Health : null;
                float ma = (ha != null) ? ha.MaxHealth : 0f;
                float mb = (hb != null) ? hb.MaxHealth : 0f;
                return mb.CompareTo(ma);
            });

            _trackedBosses.Clear();
            for (int i = 0; i < candidates.Count && i < MaxBossBars; i++)
            {
                _trackedBosses.Add(candidates[i]);
            }

            if (_trackedBosses.Count != _previousTrackedBossCount)
            {
                ModBehaviour.LogToFile($"[BossHealthHUDManager] 追踪的Boss数量变化为: {_trackedBosses.Count}");
                _previousTrackedBossCount = _trackedBosses.Count;
            }
        }

        private void UpdateBossDeathState()
        {
            if (_discoveredBosses.Count == 0) return;

            _cleanupList.Clear();

            foreach (CharacterMainControl boss in _discoveredBosses)
            {
                if (boss == null || !boss)
                {
                    _cleanupList.Add(boss);
                    continue;
                }

                Health h = boss.Health;
                if (h == null)
                {
                    _cleanupList.Add(boss);
                    continue;
                }

                float curHp = h.CurrentHealth;
                if (!_lastHpMap.TryGetValue(boss, out float prevHp))
                {
                    _lastHpMap[boss] = curHp;
                    continue;
                }

                if (prevHp > 0f && curHp <= 0f)
                {
                    string bossName = SafeGetName(boss);
                    TriggerDuckHunted(bossName);
                    _cleanupList.Add(boss);
                }

                _lastHpMap[boss] = curHp;
            }

            foreach (CharacterMainControl? dead in _cleanupList)
            {
                if (dead != null)
                {
                    _lastHpMap.Remove(dead);
                    _discoveredBosses.Remove(dead);
                    _trackedBosses.Remove(dead);
                }
            }
        }

        // ───── UGUI 创建 (已升级) ─────

        private void CreateUGUI()
        {
            GameObject canvasObj = new GameObject("BossHealthHUDCanvas");
            DontDestroyOnLoad(canvasObj);
            _canvas = canvasObj.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 1000;

            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

            canvasObj.AddComponent<GraphicRaycaster>();

            // (无改动) 血条容器
            GameObject healthBarContainer = CreateUIObject("HealthBarContainer", canvasObj.transform);
            RectTransform containerRect = healthBarContainer.GetComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.5f, 0f);
            containerRect.anchorMax = new Vector2(0.5f, 0f);
            containerRect.pivot = new Vector2(0.5f, 0f);
            containerRect.anchoredPosition = new Vector2(0, 180f);
            containerRect.sizeDelta = new Vector2(1024f, 400f);

            VerticalLayoutGroup layoutGroup = healthBarContainer.AddComponent<VerticalLayoutGroup>();
            layoutGroup.childAlignment = TextAnchor.LowerCenter;
            layoutGroup.spacing = 30f;
            layoutGroup.childControlWidth = true;
            layoutGroup.childControlHeight = false;

            float barWidth = 1024f;
            float barHeight = 32f;
            float nameHeight = 30f;

            // (无改动) 创建血条
            for (int i = 0; i < MaxBossBars; i++)
            {
                HealthBarUI ui = new HealthBarUI();

                ui.Root = CreateUIObject($"HealthBar_{i}", healthBarContainer.transform);
                ui.Root.GetComponent<RectTransform>().sizeDelta = new Vector2(barWidth, barHeight + nameHeight + 5f);
                ui.Root.AddComponent<LayoutElement>().preferredHeight = barHeight + nameHeight + 5f;

                ui.NameText = CreateUIText($"NameText_{i}", ui.Root.transform, 26, Color.white, TextAnchor.MiddleLeft);
                RectTransform nameRect = ui.NameText.GetComponent<RectTransform>();
                nameRect.anchorMin = new Vector2(0, 1);
                nameRect.anchorMax = new Vector2(0, 1);
                nameRect.pivot = new Vector2(0, 1);
                nameRect.sizeDelta = new Vector2(barWidth, nameHeight);
                nameRect.anchoredPosition = Vector2.zero;

                GameObject barContainer = CreateUIObject("BarContainer", ui.Root.transform);
                RectTransform barContainerRect = barContainer.GetComponent<RectTransform>();
                barContainerRect.anchorMin = new Vector2(0, 0);
                barContainerRect.anchorMax = new Vector2(1, 0);
                barContainerRect.pivot = new Vector2(0.5f, 0);
                barContainerRect.sizeDelta = new Vector2(0, barHeight);
                barContainerRect.anchoredPosition = new Vector2(0, 0);

                ui.BarBG = CreateUIImage($"BarBG_{i}", barContainer.transform, new Color(0.05f, 0.05f, 0.05f, 0.8f));
                StretchFillRect(ui.BarBG.GetComponent<RectTransform>());

                ui.Fill_Ghost = CreateUIImage("BarFill_Ghost", barContainer.transform, new Color(0.9f, 0.8f, 0.2f, 0.8f));
                ui.Fill_Ghost.type = Image.Type.Filled;
                ui.Fill_Ghost.fillMethod = Image.FillMethod.Horizontal;
                ui.Fill_Ghost.fillOrigin = 0;
                StretchFillRect(ui.Fill_Ghost.GetComponent<RectTransform>(), 2);

                ui.Fill = CreateUIImage("BarFill", barContainer.transform, new Color(0.7f, 0f, 0f, 0.95f));
                ui.Fill.type = Image.Type.Filled;
                ui.Fill.fillMethod = Image.FillMethod.Horizontal;
                ui.Fill.fillOrigin = 0;
                StretchFillRect(ui.Fill.GetComponent<RectTransform>(), 2);

                ui.HpText = CreateUIText($"HPText_{i}", barContainer.transform, 18, Color.white, TextAnchor.MiddleCenter);
                StretchFillRect(ui.HpText.GetComponent<RectTransform>());

                ui.Root.SetActive(false);
                _healthBarUIs.Add(ui);
            }

            // [v9 修改] DUCK HUNTED 布局 (全宽)
            _duckHuntedOverlay = CreateUIObject("DuckHuntedOverlay", canvasObj.transform);
            _duckHuntedCanvasGroup = _duckHuntedOverlay.AddComponent<CanvasGroup>();
            RectTransform overlayRect = _duckHuntedOverlay.GetComponent<RectTransform>();
            
            // --- [v9 关键修改] ---
            // 设置为水平拉伸 (full width)，Y轴居中
            overlayRect.anchorMin = new Vector2(0, 0.5f);   // 锚点: 左
            overlayRect.anchorMax = new Vector2(1, 0.5f);   // 锚点: 右
            overlayRect.pivot = new Vector2(0.5f, 0.5f); // 轴心: 中
            
            // sizeDelta.x 设为 0，使其宽度 = 屏幕宽度
            // sizeDelta.y 设为窄横幅的高度
            overlayRect.sizeDelta = new Vector2(0f, 110f); 
            overlayRect.anchoredPosition = new Vector2(0, 0); // Y轴偏移量 (0 = 屏幕正中)
            // --- [v9 修改结束] ---

            // DUCK HUNTED 背景 (自动拉伸填充父物体)
            Image duckHuntedBG = CreateUIImage("DuckHuntedBG", _duckHuntedOverlay.transform, new Color(0f, 0f, 0f, 0.65f));
            StretchFillRect(duckHuntedBG.GetComponent<RectTransform>());

            // [v6 修改] 定义淡金色
            Color paleGold = new Color(1f, 0.85f, 0.6f);

            // 主文本 (DUCK HUNTED)
            _duckHuntedMainText = CreateUIText("MainText", _duckHuntedOverlay.transform, 56, paleGold, TextAnchor.MiddleCenter);
            _duckHuntedMainText.fontStyle = FontStyle.Bold;
            _duckHuntedMainRect = _duckHuntedMainText.GetComponent<RectTransform>();
            StretchFillRect(_duckHuntedMainRect);
            _duckHuntedMainRect.sizeDelta = new Vector2(0, 80f);
            _duckHuntedMainRect.anchoredPosition = new Vector2(0, 15f); // 向上偏移

            // [v6 新增] 创建重影文本1
            _duckHuntedGhostText1 = CreateUIText("MainText_Ghost1", _duckHuntedOverlay.transform, 56, paleGold, TextAnchor.MiddleCenter);
            _duckHuntedGhostText1.fontStyle = FontStyle.Bold;
            _duckHuntedGhost1Rect = _duckHuntedGhostText1.GetComponent<RectTransform>();
            StretchFillRect(_duckHuntedGhost1Rect);
            _duckHuntedGhost1Rect.sizeDelta = _duckHuntedMainRect.sizeDelta;
            _duckHuntedGhost1Rect.anchoredPosition = _duckHuntedMainRect.anchoredPosition;

            // [v6 新增] 创建重影文本2
            _duckHuntedGhostText2 = CreateUIText("MainText_Ghost2", _duckHuntedOverlay.transform, 56, paleGold, TextAnchor.MiddleCenter);
            _duckHuntedGhostText2.fontStyle = FontStyle.Bold;
            _duckHuntedGhost2Rect = _duckHuntedGhostText2.GetComponent<RectTransform>();
            StretchFillRect(_duckHuntedGhost2Rect);
            _duckHuntedGhost2Rect.sizeDelta = _duckHuntedMainRect.sizeDelta;
            _duckHuntedGhost2Rect.anchoredPosition = _duckHuntedMainRect.anchoredPosition;

            // 副文本 (Boss 名字)
            _duckHuntedSubText = CreateUIText("SubText", _duckHuntedOverlay.transform, 26, Color.white, TextAnchor.MiddleCenter);
            RectTransform subTextRect = _duckHuntedSubText.GetComponent<RectTransform>();
            StretchFillRect(subTextRect);
            subTextRect.sizeDelta = new Vector2(0, 40f);
            subTextRect.anchoredPosition = new Vector2(0, -25f); // 向下偏移

            _duckHuntedOverlay.SetActive(false);
            ModBehaviour.LogToFile("[BossHealthHUDManager] UGUI 已创建并初始化。");
        }

        private void StretchFillRect(RectTransform rect, float margin = 0)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(margin, margin);
            rect.offsetMax = new Vector2(-margin, -margin);
        }

        private GameObject CreateUIObject(string name, Transform parent)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<RectTransform>();
            return obj;
        }

        // [v3] 已修复的 Sprite 创建
        private Image CreateUIImage(string name, Transform parent, Color color)
        {
            Image img = CreateUIObject(name, parent).AddComponent<Image>();
            img.color = color;

            if (_minimalSprite == null)
            {
                ModBehaviour.LogToFile("[BossHealthHUDManager] 正在创建 1x1 像素 Sprite...");
                var tex = new Texture2D(1, 1);
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                _minimalSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
            }

            img.sprite = _minimalSprite;
            return img;
        }

        private Text CreateUIText(string name, Transform parent, int fontSize, Color color, TextAnchor alignment)
        {
            Text txt = CreateUIObject(name, parent).AddComponent<Text>();
            txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            txt.fontSize = fontSize;
            txt.color = color;
            txt.alignment = alignment;

            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;

            var shadow = txt.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.5f);
            shadow.effectDistance = new Vector2(1, -1);
            return txt;
        }

        // ───── UGUI 更新 (v3 - 虚血逻辑) ─────

        private void UpdateUGUI()
        {
            if (_player == null)
            {
                if (_previousDrawnBossCount != 0)
                {
                    for (int i = 0; i < _healthBarUIs.Count; i++) _healthBarUIs[i].Root.SetActive(false);
                    ModBehaviour.LogToFile("[BossHealthHUDManager] UpdateUGUI: Player 为 null，隐藏所有血条。");
                    _previousDrawnBossCount = 0;
                }
                return;
            }

            int drawnCount = 0;
            for (int i = 0; i < _healthBarUIs.Count; i++)
            {
                if (i < _trackedBosses.Count && drawnCount < MaxBossBars)
                {
                    CharacterMainControl boss = _trackedBosses[i];
                    HealthBarUI ui = _healthBarUIs[i];

                    if (boss == null || !boss || boss.Health == null || boss.Health.CurrentHealth <= 0f)
                    {
                        ui.Root.SetActive(false);
                        continue;
                    }

                    bool wasJustActivated = !ui.Root.activeSelf;

                    float maxHp = boss.Health.MaxHealth;
                    float curHp = boss.Health.CurrentHealth;
                    float targetRatio = Mathf.Clamp01(curHp / maxHp);

                    if (wasJustActivated)
                    {
                        ui.CurrentGhostFill = targetRatio;
                        ui.LastKnownFill = targetRatio;
                        ui.GhostTimer = 0f;
                    }

                    ui.Fill.fillAmount = targetRatio;

                    if (targetRatio < ui.LastKnownFill)
                    {
                        ui.GhostTimer = HealthBarUI.GhostDelay;
                    }
                    else if (targetRatio > ui.CurrentGhostFill)
                    {
                        ui.CurrentGhostFill = targetRatio;
                        ui.GhostTimer = 0f;
                    }

                    if (ui.GhostTimer > 0f)
                    {
                        ui.GhostTimer -= Time.deltaTime;
                    }
                    else
                    {
                        if (ui.CurrentGhostFill > targetRatio)
                        {
                            ui.CurrentGhostFill = Mathf.MoveTowards(
                                ui.CurrentGhostFill,
                                targetRatio,
                                HealthBarUI.GhostLerpSpeed * Time.deltaTime
                            );
                        }
                        else
                        {
                            ui.CurrentGhostFill = targetRatio;
                        }
                    }

                    ui.Fill_Ghost.fillAmount = ui.CurrentGhostFill;
                    ui.LastKnownFill = targetRatio;

                    ui.NameText.text = SafeGetName(boss);
                    ui.HpText.text = string.Format("{0:0}/{1:0}  ({2:P0})", curHp, maxHp, targetRatio);
                    ui.Root.SetActive(true);

                    drawnCount++;
                }
                else
                {
                    _healthBarUIs[i].Root.SetActive(false);
                }
            }

            if (drawnCount != _previousDrawnBossCount)
            {
                ModBehaviour.LogToFile($"[BossHealthHUDManager] UpdateUGUI: 正在绘制的血条数量变化为: {drawnCount}");
                _previousDrawnBossCount = drawnCount;
            }
        }

        // ───── 辅助方法 ─────

        private void TryFindPlayer()
        {
            try { _player = CharacterMainControl.Main; }
            catch (Exception) { /* 忽略 */ }
        }

        // [v6 修改]
        private void TriggerDuckHunted(string bossName)
        {
            // [v6] 停止任何正在播放的旧动画
            if (_duckHuntedCoroutine != null)
            {
                StopCoroutine(_duckHuntedCoroutine);
            }

            _lastKilledBossName = bossName;
            
            // 设置所有文本
            _duckHuntedMainText.text = "DUCK HUNTED";
            _duckHuntedGhostText1.text = "DUCK HUNTED";
            _duckHuntedGhostText2.text = "DUCK HUNTED";
            _duckHuntedSubText.text = bossName;

            _duckHuntedOverlay.SetActive(true);

            // [v6] 启动新的动画协程
            _duckHuntedCoroutine = StartCoroutine(AnimateDuckHunted());

            ModBehaviour.LogToFile($"[BossHealthHUDManager] DUCK HUNTED -> {bossName}");
            TryPlayBossDefeatedSound();
        }

        // [v6 新增] "黑魂" 风格的动画协程
        private IEnumerator AnimateDuckHunted()
        {
            float timer = 0f;

            Vector2 mainPos = _duckHuntedMainRect.anchoredPosition;
            Color baseColor = _duckHuntedMainText.color;

            // --- 1. 汇聚阶段 (GhostConvergeTime = 1.0s) --- [v7 修改]
            // 此阶段同时处理：整体淡入、重影汇聚、重影淡出
            while (timer < GhostConvergeTime)
            {
                float t = timer / GhostConvergeTime; // 进程 (0 -> 1)

                // A. 整体 Alpha (淡入)
                _duckHuntedCanvasGroup.alpha = t;

                // B. 重影位置 (汇聚)
                float offset = Mathf.Lerp(GhostMaxOffset, 0f, t);
                _duckHuntedGhost1Rect.anchoredPosition = mainPos + new Vector2(offset, 0);
                _duckHuntedGhost2Rect.anchoredPosition = mainPos + new Vector2(-offset, 0);

                // C. 重影 Alpha (重影淡出)
                float ghostAlpha = Mathf.Lerp(0.5f, 0f, t);
                _duckHuntedGhostText1.color = new Color(baseColor.r, baseColor.g, baseColor.b, ghostAlpha);
                _duckHuntedGhostText2.color = new Color(baseColor.r, baseColor.g, baseColor.b, ghostAlpha);

                timer += Time.deltaTime;
                yield return null; // 等待下一帧
            }

            // --- 汇聚阶段 最终清理 ---
            _duckHuntedCanvasGroup.alpha = 1f;
            _duckHuntedGhost1Rect.anchoredPosition = mainPos;
            _duckHuntedGhost2Rect.anchoredPosition = mainPos;
            _duckHuntedGhostText1.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0f);
            _duckHuntedGhostText2.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0f);

            // --- 2. 保持阶段 (GhostHoldTime = 0.5s) --- [v7 修改]
            yield return new WaitForSeconds(GhostHoldTime);

            // --- 3. 淡出阶段 (FadeOutTime = 1.0s) ---
            timer = 0f;
            while (timer < FadeOutTime)
            {
                float t = timer / FadeOutTime; // 进程 (0 -> 1)
                _duckHuntedCanvasGroup.alpha = Mathf.Lerp(1f, 0f, t); // Alpha (1 -> 0)

                timer += Time.deltaTime;
                yield return null; // 等待下一帧
            }

            // --- 4. 动画结束 最终清理 ---
            _duckHuntedCanvasGroup.alpha = 0f;
            _duckHuntedOverlay.SetActive(false);
            _duckHuntedCoroutine = null;
        }


        private void TryPlayBossDefeatedSound()
        {
            try
            {
                string dllPath = Assembly.GetExecutingAssembly().Location;
                string folder = Path.GetDirectoryName(dllPath);
                if (string.IsNullOrEmpty(folder)) return;
                string audioDir = Path.Combine(folder, "Audio");
                string filePath = Path.Combine(audioDir, "BossDefeated.mp3");
                if (!System.IO.File.Exists(filePath))
                {
                    ModBehaviour.LogToFile("[BossHealthHUDManager] BossDefeated.mp3 未找到: " + filePath);
                    return;
                }
                AudioManager.PostCustomSFX(filePath, null, false);
            }
            catch (Exception ex)
            {
                ModBehaviour.LogErrorToFile("[BossHealthHUDManager] TryPlayBossDefeatedSound 错误: " + ex);
            }
        }

        private static string SafeGetName(CharacterMainControl ch)
        {
            if (ch == null) return string.Empty;
            try
            {
                if (ch.characterPreset != null && !string.IsNullOrEmpty(ch.characterPreset.DisplayName))
                {
                    return ch.characterPreset.DisplayName;
                }
            }
            catch { }

            return ch.name;
        }
    }
}