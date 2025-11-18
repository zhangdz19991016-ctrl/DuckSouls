using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Duckov.Modding;
using UnityEngine;
using HarmonyLib;

// 确保命名空间是 DuckSoulsDLC
namespace DuckSoulsDLC
{
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        // 模组的全局静态实例，方便子系统访问
        public static ModBehaviour Instance { get; private set; }
        
        // 声明咒术管理器
        internal SpellManager _spellManager;
        
        // 声明武器技能管理器
        internal WeaponSkillManager _weaponSkillManager; 
        
        /// <summary>
        /// [职责] 初始化实例，创建所有管理器
        /// </summary>
        void Awake()
        {
            Instance = this;
            
            // 1. 创建咒术系统实例
            _spellManager = new SpellManager(this);
            
            // 2. 创建武器技能系统实例
            _weaponSkillManager = new WeaponSkillManager(this);
        }

        /// <summary>
        /// [职责] 启动所有系统
        /// </summary>
        protected override void OnAfterSetup()
        {
            base.OnAfterSetup();
            
            // 启动咒术系统
            _spellManager.OnAfterSetup(); 
            
            // 启动武器技能系统
            _weaponSkillManager.OnAfterSetup();
        }
        
        void OnEnable()
        {
            // (空)
        }

        /// <summary>
        /// [职责] 关闭所有系统
        /// </summary>
        protected override void OnBeforeDeactivate()
        {
            base.OnBeforeDeactivate();
            
            // 关闭咒术系统
            _spellManager.OnBeforeDeactivate();

            // 关闭武器技能系统
            _weaponSkillManager.OnBeforeDeactivate();
        }
    }
}