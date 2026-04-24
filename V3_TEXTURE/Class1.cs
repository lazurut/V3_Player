using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Player_V3
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static string TextureFolderPath;

        // ── Textures only for v1_mdl ─────────────────────────────────────────
        internal static readonly Dictionary<string, string> V1ModelTextureMap = new Dictionary<string, string>
        {
            { "v1colour_tex",      "v3colour_tex"      },
            { "v1_wingcolour_tex", "v3_wingcolour_tex" }
        };

        // ── Textures for v1_combined and the entire scene ────────────────────
        internal static readonly Dictionary<string, string> SceneTextureMap = new Dictionary<string, string>
        {
            { "v2_armtex",       "v3_armtex"        },
            { "T_Feedbacker",    "T_FireArm"         },
            { "T_GreenArm",      "T_PurpleArm"       },
            // Textmode
            { "TextmodeV1",      "TextmodeV3"        },
            { "TextmodeV1Arm1",  "TextmodeV3Arm1"    },
            { "TextmodeV1Arm2",  "TextmodeV3Arm2"    },
            { "TextmodeV1Wings", "TextmodeV3Wings"   },
            { "TextmodeCircuit", "V3TextmodeCircuit" },
            { "TextmodeLogo",    "TextmodeLogoV3"    }
        };

        // ── Textures only for Canvas UI (Textmode) ───────────────────────────
        internal static readonly Dictionary<string, string> TextmodeUIMap = new Dictionary<string, string>
        {
            { "TextmodeV1",      "TextmodeV3"        },
            { "TextmodeV1Arm1",  "TextmodeV3Arm1"    },
            { "TextmodeV1Arm2",  "TextmodeV3Arm2"    },
            { "TextmodeV1Wings", "TextmodeV3Wings"   },
            { "TextmodeCircuit", "V3TextmodeCircuit" },
            { "TextmodeLogo",    "TextmodeLogoV3"    }
        };

        // ── Textures only for scene 36abcaae9708abc4d9e89e6ec73a2846 ─────────
        internal static readonly Dictionary<string, string> V2ToV1TextureMap = new Dictionary<string, string>
        {
            { "v2colour_tex",       "v1colour_tex"      },
            { "v2_wingcolour_tex",  "v1_wingcolour_tex" },
            { "v2_wingcolour_tex 3","v1_wingcolour_tex" },
            { "v2_wingcolour_tex 2","v1_wingcolour_tex" },
            { "v2_wingcolour_tex 1","v1_wingcolour_tex" }
        };

        // ── Unified cache of all loaded textures ─────────────────────────────
        internal static readonly Dictionary<string, Texture2D> LoadedTextures = new Dictionary<string, Texture2D>();

        private void Awake()
        {
            Log = Logger;
            TextureFolderPath = Path.Combine(Path.GetDirectoryName(Info.Location), "texturesPlayer_V3");

            if (!Directory.Exists(TextureFolderPath))
            {
                Log.LogError($"[Player_V3] Folder not found: {TextureFolderPath}");
                return;
            }

            PreloadTextures(V1ModelTextureMap);
            PreloadTextures(SceneTextureMap);
            PreloadTextures(TextmodeUIMap);
            PreloadTextures(V2ToV1TextureMap);

            SceneManager.sceneLoaded += OnSceneLoaded;

            new Harmony(PluginInfo.PLUGIN_GUID).PatchAll();

            Log.LogInfo("[Player_V3] Mod loaded!");
        }

        private const string TARGET_SCENE = "b3e7f2f8052488a45b35549efb98d902";
        private const string V2_TO_V1_SCENE = "36abcaae9708abc4d9e89e6ec73a2846";

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Log.LogInfo($"[Player_V3] Scene: {scene.name}");
            StartCoroutine(ReplaceAfterFrame(scene.name));
        }

        private IEnumerator ReplaceAfterFrame(string sceneName)
        {
            yield return null;
            yield return null;

            // 1. Textures for v1_mdl
            ReplaceOnV1Model();

            // 2. Textures for v1_combined (NO scale — scale only via Harmony patch)
            HandleV1Combined();

            // 3. SceneTextureMap textures scene-wide
            ReplaceSceneWide();

            // 4. RightArm → T_FireArm on all scenes
            ReplaceRightArmTexture();

            // 5. UI text replacement on all scenes
            ReplaceUIText();

            // 6. V2→V1 only on the relevant scene
            if (sceneName == V2_TO_V1_SCENE)
            {
                Log.LogInfo("[Player_V3] V2→V1 scene detected — replacing v2colour/wing textures...");
                ReplaceV2ToV1SceneWide();
                StartCoroutine(RepeatReplaceV2ToV1());
            }

            if (sceneName == TARGET_SCENE)
            {
                Log.LogInfo($"[Player_V3] Target scene detected — replacing Canvas UI and starting repeat passes...");
                ReplaceCanvasUI();
                StartCoroutine(RepeatReplaceForTargetScene());
            }
        }

        private IEnumerator RepeatReplaceForTargetScene()
        {
            yield return new WaitForSeconds(0.5f);
            Log.LogInfo("[Player_V3] Repeat pass #1...");
            ReplaceSceneWide();
            ReplaceCanvasUI();
            ReplaceRightArmTexture();
            ReplaceUIText();

            yield return new WaitForSeconds(0.5f);
            Log.LogInfo("[Player_V3] Repeat pass #2...");
            ReplaceSceneWide();
            ReplaceCanvasUI();
            ReplaceRightArmTexture();
            ReplaceUIText();

            yield return new WaitForSeconds(1.0f);
            Log.LogInfo("[Player_V3] Repeat pass #3 (final)...");
            ReplaceSceneWide();
            ReplaceCanvasUI();
            ReplaceRightArmTexture();
            ReplaceUIText();
        }

        // ── Repeat passes V2→V1 (objects may load with a delay) ─────────────
        private IEnumerator RepeatReplaceV2ToV1()
        {
            yield return new WaitForSeconds(0.5f);
            Log.LogInfo("[Player_V3] V2→V1 repeat pass #1...");
            ReplaceV2ToV1SceneWide();
            ReplaceRightArmTexture();
            ReplaceUIText();

            yield return new WaitForSeconds(0.5f);
            Log.LogInfo("[Player_V3] V2→V1 repeat pass #2...");
            ReplaceV2ToV1SceneWide();
            ReplaceRightArmTexture();
            ReplaceUIText();

            yield return new WaitForSeconds(1.0f);
            Log.LogInfo("[Player_V3] V2→V1 repeat pass #3 (final)...");
            ReplaceV2ToV1SceneWide();
            ReplaceRightArmTexture();
            ReplaceUIText();
        }

        // ── V2→V1 replacement scene-wide ─────────────────────────────────────
        public static void ReplaceV2ToV1SceneWide()
        {
            int replaced = 0;
            foreach (var rend in FindObjectsOfType<Renderer>(true))
            {
                Material[] mats = rend.sharedMaterials;
                bool dirty = false;
                for (int i = 0; i < mats.Length; i++)
                    if (ApplyToMaterial(mats[i], V2ToV1TextureMap)) { replaced++; dirty = true; }
                if (dirty) rend.sharedMaterials = mats;
            }
            Log.LogInfo($"[Player_V3] V2→V1 replacements: {replaced}");
        }

        // ── RightArm → T_FireArm on all scenes ───────────────────────────────
        public static void ReplaceRightArmTexture()
        {
            // T_FireArm is loaded as the value for key "T_Feedbacker"
            if (!LoadedTextures.TryGetValue("T_Feedbacker", out Texture2D fireArmTex))
            {
                Log.LogWarning("[Player_V3] T_FireArm not found in cache (requires T_FireArm.png in texturesPlayer_V3).");
                return;
            }

            GameObject rightArm = FindObject("RightArm");
            if (rightArm == null)
            {
                Log.LogWarning("[Player_V3] Object 'RightArm' not found in scene.");
                return;
            }

            int replaced = 0;
            foreach (var rend in rightArm.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = rend.sharedMaterials;
                bool dirty = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    foreach (string propName in mats[i].GetTexturePropertyNames())
                    {
                        Texture cur = mats[i].GetTexture(propName);
                        if (cur == null) continue;
                        mats[i].SetTexture(propName, fireArmTex);
                        Log.LogInfo(
                            $"[Player_V3] RightArm [{rend.name}].[{propName}]: " +
                            $"{cur.name} → {fireArmTex.name}");
                        dirty = true;
                        replaced++;
                    }
                }
                if (dirty) rend.sharedMaterials = mats;
            }
            Log.LogInfo($"[Player_V3] RightArm replacements: {replaced}");
        }

        // ── UI text replacement dictionary ───────────────────────────────────
        // Order matters: more specific/longer strings first,
        // so "V1" doesn't match before strings that contain it.
        private static readonly (string from, string to)[] UITextReplacements =
        {
            ("Find\u00a0a weapon",    "liquidation V1"),   // non-breaking space (in-game variant)
            ("Find a weapon",         "liquidation V1"),   // regular space
            ("Find\u0430 weapon",     "liquidation V1"),   // Cyrillic 'a' (just in case)
            ("Hell is Full",          "THIS SCUM WILL BE DESTROYED"),
            ("(2112.08.06)",          "(2112.09.13)"),
            ("V1",                    "V3"),
            ("v1",                    "v3"),
        };

        public static void ReplaceUIText()
        {
            int replaced = 0;

            // ── UnityEngine.UI.Text ──────────────────────────────────────────
            foreach (var label in FindObjectsOfType<Text>(true))
            {
                string original = label.text;
                string updated = ApplyTextReplacements(original);
                if (updated != original)
                {
                    Log.LogInfo(
                        $"[Player_V3] UI.Text [{label.gameObject.name}]: " +
                        $"\"{original}\" → \"{updated}\"");
                    label.text = updated;
                    replaced++;
                }
            }

            // ── TextMeshPro (world-space and UI) ─────────────────────────────
            foreach (var tmp in FindObjectsOfType<TMP_Text>(true))
            {
                string original = tmp.text;
                string updated = ApplyTextReplacements(original);
                if (updated != original)
                {
                    Log.LogInfo(
                        $"[Player_V3] TMP [{tmp.gameObject.name}]: " +
                        $"\"{original}\" → \"{updated}\"");
                    tmp.text = updated;
                    replaced++;
                }
            }

            Log.LogInfo($"[Player_V3] UI texts replaced: {replaced}");
        }

        private static string ApplyTextReplacements(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            foreach (var (from, to) in UITextReplacements)
                text = text.Replace(from, to);
            return text;
        }

        public static void ReplaceCanvasUI()
        {
            int replaced = 0;

            foreach (var rawImg in FindObjectsOfType<RawImage>(true))
            {
                if (rawImg.texture == null) continue;
                if (TextmodeUIMap.ContainsKey(rawImg.texture.name) &&
                    LoadedTextures.TryGetValue(rawImg.texture.name, out Texture2D replacement))
                {
                    Log.LogInfo(
                        $"[Player_V3] RawImage [{rawImg.gameObject.name}]: " +
                        $"{rawImg.texture.name} → {replacement.name}");
                    rawImg.texture = replacement;
                    replaced++;
                }
            }

            foreach (var img in FindObjectsOfType<Image>(true))
            {
                if (img.sprite == null || img.sprite.texture == null) continue;
                if (TextmodeUIMap.ContainsKey(img.sprite.texture.name) &&
                    LoadedTextures.TryGetValue(img.sprite.texture.name, out Texture2D replacement))
                {
                    Log.LogInfo(
                        $"[Player_V3] Image [{img.gameObject.name}]: " +
                        $"{img.sprite.texture.name} → {replacement.name}");
                    Rect rect = new Rect(0, 0, replacement.width, replacement.height);
                    img.sprite = Sprite.Create(replacement, rect, new Vector2(0.5f, 0.5f));
                    replaced++;
                }
            }

            Log.LogInfo($"[Player_V3] Canvas UI replacements: {replaced}");
        }

        public static void ReplaceOnV1Model()
        {
            GameObject v1mdl = FindObject("v1_mdl");
            if (v1mdl == null)
            {
                Log.LogWarning("[Player_V3] 'v1_mdl' not found in scene.");
                return;
            }
            Log.LogInfo("[Player_V3] Processing v1_mdl...");
            ApplyToGameObject(v1mdl, V1ModelTextureMap);
        }

        public static void HandleV1Combined()
        {
            GameObject v1combined = FindObject("v1_combined");
            if (v1combined == null)
            {
                Log.LogWarning("[Player_V3] 'v1_combined' not found in scene.");
                return;
            }

            Log.LogInfo("[Player_V3] Processing v1_combined textures (scale — only via Harmony)...");
            ApplyToGameObject(v1combined, SceneTextureMap);
        }

        public static void ReplaceSceneWide()
        {
            int replaced = 0;
            foreach (var rend in FindObjectsOfType<Renderer>(true))
            {
                Material[] mats = rend.sharedMaterials;
                bool dirty = false;
                for (int i = 0; i < mats.Length; i++)
                    if (ApplyToMaterial(mats[i], SceneTextureMap)) { replaced++; dirty = true; }
                if (dirty) rend.sharedMaterials = mats;
            }
            Log.LogInfo($"[Player_V3] Scene-wide replacements: {replaced}");
        }

        public static void ApplyToGameObject(GameObject go, Dictionary<string, string> map)
        {
            int replaced = 0;
            foreach (var rend in go.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = rend.sharedMaterials;
                bool dirty = false;
                for (int i = 0; i < mats.Length; i++)
                    if (ApplyToMaterial(mats[i], map)) { replaced++; dirty = true; }
                if (dirty) rend.sharedMaterials = mats;
            }
            Log.LogInfo($"[Player_V3] [{go.name}] materials replaced: {replaced}");
        }

        public static bool ApplyToMaterial(Material mat, Dictionary<string, string> map)
        {
            if (mat == null) return false;
            bool changed = false;

            foreach (string propName in mat.GetTexturePropertyNames())
            {
                Texture currentTex = mat.GetTexture(propName);
                if (currentTex == null) continue;

                if (map.ContainsKey(currentTex.name) &&
                    LoadedTextures.TryGetValue(currentTex.name, out Texture2D replacement))
                {
                    mat.SetTexture(propName, replacement);
                    Log.LogInfo(
                        $"[Player_V3] [{mat.name}].[{propName}]: " +
                        $"{currentTex.name} → {replacement.name}");
                    changed = true;
                }
            }

            return changed;
        }

        public static GameObject FindObject(string targetName)
        {
            GameObject found = GameObject.Find(targetName);
            if (found != null) return found;

            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                GameObject result = FindInChildren(root.transform, targetName);
                if (result != null) return result;
            }
            return null;
        }

        private static GameObject FindInChildren(Transform parent, string targetName)
        {
            if (parent.name == targetName) return parent.gameObject;
            foreach (Transform child in parent)
            {
                GameObject result = FindInChildren(child, targetName);
                if (result != null) return result;
            }
            return null;
        }

        private static void PreloadTextures(Dictionary<string, string> map)
        {
            string[] extensions = { ".png", ".jpg", ".jpeg" };

            foreach (var pair in map)
            {
                if (LoadedTextures.ContainsKey(pair.Key)) continue;

                bool found = false;
                foreach (string ext in extensions)
                {
                    string path = Path.Combine(TextureFolderPath, pair.Value + ext);
                    if (!File.Exists(path)) continue;

                    Texture2D tex = LoadPNG(path);
                    if (tex != null)
                    {
                        tex.name = pair.Value;
                        LoadedTextures[pair.Key] = tex;
                        Log.LogInfo($"[Player_V3] ✓ {pair.Key} → {pair.Value}{ext}");
                        found = true;
                    }
                    break;
                }

                if (!found)
                    Log.LogWarning($"[Player_V3] ✗ File not found: {pair.Value} (.png/.jpg)");
            }
        }

        public static Texture2D LoadPNG(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (tex.LoadImage(data))
            {
                tex.Apply(true, false);
                return tex;
            }
            Log.LogError($"[Player_V3] Failed to decode: {path}");
            return null;
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  MonoBehaviour for light control (X = create/remove Point Light)
    // ────────────────────────────────────────────────────────────────────────
    public class V3LightController : MonoBehaviour
    {
        private Light _pointLight;

        private void Update()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.X))
            {
                if (_pointLight == null)
                    CreateLight();
                else
                    ToggleLight();
            }
        }

        private void CreateLight()
        {
            Transform v1mdl = null;
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "v1_mdl") { v1mdl = child; break; }
            }

            Transform parent = v1mdl != null ? v1mdl : transform;

            GameObject lightGO = new GameObject("Player_V3_PointLight");
            lightGO.transform.SetParent(parent, false);
            lightGO.transform.localPosition = Vector3.zero;

            _pointLight = lightGO.AddComponent<Light>();
            _pointLight.type = LightType.Point;
            _pointLight.color = Color.green;
            _pointLight.range = 200f;
            _pointLight.renderMode = LightRenderMode.ForcePixel;
            _pointLight.enabled = true;

            Plugin.Log.LogInfo(
                $"[Player_V3] Point Light created on '{parent.name}' " +
                $"(color=green, range=200, mode=Important)");
        }

        private void ToggleLight()
        {
            _pointLight.enabled = !_pointLight.enabled;
            Plugin.Log.LogInfo(
                $"[Player_V3] Point Light: {(_pointLight.enabled ? "enabled" : "disabled")}");
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Metadata
    // ────────────────────────────────────────────────────────────────────────
    internal static class PluginInfo
    {
        internal const string PLUGIN_GUID = "com.lazurut.ultrakill.player_v3";
        internal const string PLUGIN_NAME = "PLAYER V3";
        internal const string PLUGIN_VERSION = "0.0.2";
    }
}