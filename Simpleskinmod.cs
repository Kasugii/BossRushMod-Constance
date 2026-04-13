using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using System.Collections;
using System;
using System.Linq;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

namespace ConstanceSkinMod
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class ConstanceSkinPermanent : BaseUnityPlugin
    {
        public const string PluginGUID = "com.modder.constance.skinperm";
        public const string PluginName = "Constance Skin Permanent";
        public const string PluginVersion = "3.1.0";

        private static ManualLogSource Log;
        private bool skinEnabled = false;
        private bool isInGameScene = false;
        private Material corruptedMaterial = null;
        private Material originalMaterial = null;
        private Renderer[] playerRenderers = null;
        private Coroutine maintainCoroutine = null;

        void Awake()
        {
            Log = Logger;
            Log.LogInfo("====================================");
            Log.LogInfo("=== SKIN PERMANENT v3.1.0 ===");
            Log.LogInfo("====================================");
            Log.LogInfo("F3 = Toggle (stays active PERMANENTLY)");

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Log.LogInfo($"Scene: {scene.name}");

            bool wasInGame = isInGameScene;
            isInGameScene = scene.name.Contains("Prod_") ||
                           scene.name.Contains("Level_") ||
                           scene.name.Contains("Town");

            if (isInGameScene && !wasInGame)
            {
                Log.LogInfo("=== IN GAME ! F3 to enable permanent skin ===");

                // Reset
                playerRenderers = null;
                corruptedMaterial = null;
                originalMaterial = null;
            }
        }

        void Update()
        {
            if (!isInGameScene) return;

            try
            {
                if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
                {
                    skinEnabled = !skinEnabled;
                    Log.LogInfo("====================================");
                    Log.LogInfo($"Skin {(skinEnabled ? "ON" : "OFF")}");
                    Log.LogInfo("====================================");

                    if (skinEnabled)
                    {
                        StartCoroutine(ApplySkin());
                    }
                    else
                    {
                        // Stop maintenance
                        if (maintainCoroutine != null)
                        {
                            StopCoroutine(maintainCoroutine);
                            maintainCoroutine = null;
                        }

                        // Restore original
                        RestoreOriginalMaterial();
                    }
                }
            }
            catch (Exception e)
            {
                Log.LogError($"Update error: {e.Message}");
            }
        }

        private IEnumerator ApplySkin()
        {
            yield return new WaitForSeconds(0.5f);

            for (int i = 0; i < 10; i++)
            {
                Log.LogInfo($"Attempt {i + 1}/10...");

                bool success = TryApplyVisualSkin();

                if (success)
                {
                    Log.LogInfo("====================================");
                    Log.LogInfo("===   PERMANENT SKIN ACTIVE !   ===");
                    Log.LogInfo("====================================");

                    // Start PERMANENT MAINTENANCE
                    if (maintainCoroutine != null)
                    {
                        StopCoroutine(maintainCoroutine);
                    }
                    maintainCoroutine = StartCoroutine(MaintainSkinPermanently());

                    yield break;
                }

                yield return new WaitForSeconds(1f);
            }

            Log.LogError("Failed after 10 attempts");
        }

        private bool TryApplyVisualSkin()
        {
            try
            {
                // 1. Find the player
                object player = FindPlayer();
                if (player == null)
                {
                    Log.LogWarning("Player not found");
                    return false;
                }

                Log.LogInfo("Player found!");

                // 2. Paint component
                var paintProp = player.GetType().GetProperty("Paint");
                if (paintProp == null)
                {
                    Log.LogError("No Paint property");
                    return false;
                }

                object paint = paintProp.GetValue(player);
                if (paint == null)
                {
                    Log.LogWarning("Paint null");
                    return false;
                }

                Log.LogInfo("Paint OK!");

                // 3. Corrupted material
                var corruptedMatField = paint.GetType().GetField("corruptedMaterial",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (corruptedMatField == null)
                {
                    Log.LogError("corruptedMaterial field not found");
                    return false;
                }

                corruptedMaterial = corruptedMatField.GetValue(paint) as Material;

                if (corruptedMaterial == null)
                {
                    Log.LogError("corruptedMaterial null");
                    return false;
                }

                Log.LogInfo($"Corrupted material : {corruptedMaterial.name}");

                // 4. Renderers
                var playerComponent = player as Component;
                if (playerComponent == null)
                {
                    Log.LogError("Player is not a Component");
                    return false;
                }

                // Filter ONLY normal renderers (exclude VFXRenderer !)
                playerRenderers = playerComponent.GetComponentsInChildren<Renderer>()
                    .Where(r => r != null && r.GetType().Name != "VFXRenderer")
                    .ToArray();

                if (playerRenderers == null || playerRenderers.Length == 0)
                {
                    Log.LogError("No renderers found");
                    return false;
                }

                Log.LogInfo($"{playerRenderers.Length} normal renderers (VFX excluded)");

                // 5. Save the original
                if (originalMaterial == null && playerRenderers.Length > 0)
                {
                    originalMaterial = playerRenderers[0].sharedMaterial;
                    Log.LogInfo($"Original material : {originalMaterial?.name}");
                }

                // 6. Apply the skin
                ApplyCorruptedMaterial();

                return true;
            }
            catch (Exception e)
            {
                Log.LogError($"ERROR: {e.Message}");
                return false;
            }
        }

        private void ApplyCorruptedMaterial()
        {
            if (playerRenderers == null || corruptedMaterial == null) return;

            foreach (var renderer in playerRenderers)
            {
                if (renderer != null)
                {
                    renderer.sharedMaterial = corruptedMaterial;
                }
            }
        }

        private void RestoreOriginalMaterial()
        {
            if (playerRenderers == null || originalMaterial == null) return;

            foreach (var renderer in playerRenderers)
            {
                if (renderer != null)
                {
                    renderer.sharedMaterial = originalMaterial;
                }
            }

            Log.LogInfo("Original material restored");
        }

        /// <summary>
        /// PERMANENT MAINTENANCE - Forces the material every frame
        /// </summary>
        private IEnumerator MaintainSkinPermanently()
        {
            Log.LogInfo("=== PERMANENT MAINTENANCE STARTED ===");
            Log.LogInfo("The skin will remain active even if the game tries to change it!");

            while (skinEnabled && isInGameScene)
            {
                try
                {
                    // Check ALL renderers
                    if (playerRenderers != null && corruptedMaterial != null)
                    {
                        bool needFix = false;

                        foreach (var renderer in playerRenderers)
                        {
                            if (renderer != null && renderer.sharedMaterial != corruptedMaterial)
                            {
                                needFix = true;
                                break;
                            }
                        }

                        if (needFix)
                        {
                            // The game changed the material, force it again (silently)
                            ApplyCorruptedMaterial();
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.LogError($"Maintenance error: {e.Message}");
                }

                // Check every 0.1 seconds to stay responsive
                yield return new WaitForSeconds(0.1f);
            }

            Log.LogInfo("=== MAINTENANCE STOPPED ===");
            maintainCoroutine = null;
        }

        private object FindPlayer()
        {
            try
            {
                // CConSceneRegistry
                Type registryType = null;

                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        foreach (var type in assembly.GetTypes())
                        {
                            if (type.Name == "CConSceneRegistry")
                            {
                                registryType = type;
                                break;
                            }
                        }
                        if (registryType != null) break;
                    }
                    catch { continue; }
                }

                if (registryType == null) return null;

                // Instance
                var instanceProp = registryType.GetProperty("Instance",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                if (instanceProp == null) return null;

                object registry = instanceProp.GetValue(null);
                if (registry == null) return null;

                // PlayerManager
                var getMethod = registry.GetType().GetMethod("Get",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                if (getMethod == null) return null;

                // IConPlayerManager
                Type playerManagerInterface = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        foreach (var type in assembly.GetTypes())
                        {
                            if (type.Name == "IConPlayerManager")
                            {
                                playerManagerInterface = type;
                                break;
                            }
                        }
                        if (playerManagerInterface != null) break;
                    }
                    catch { continue; }
                }

                if (playerManagerInterface == null) return null;

                var genericGet = getMethod.MakeGenericMethod(playerManagerInterface);
                object playerManager = genericGet.Invoke(registry, null);

                if (playerManager == null) return null;

                // GetPlayerOne
                var getPlayerOneMethod = playerManager.GetType().GetMethod("GetPlayerOne");
                if (getPlayerOneMethod == null) return null;

                object playerInfo = getPlayerOneMethod.Invoke(playerManager, null);
                if (playerInfo == null) return null;

                // Entity
                var entityProp = playerInfo.GetType().GetProperty("Entity");
                if (entityProp == null) return null;

                return entityProp.GetValue(playerInfo);
            }
            catch
            {
                return null;
            }
        }
    }
}