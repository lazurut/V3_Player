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
using UnityEngine.Video;

namespace Player_V3
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static string TextureFolderPath;
        internal static string VideoFolderPath;

        // ── Бандл ────────────────────────────────────────────────────────────
        private const string BUNDLE_FOLDER = "Lazurut-PLAYER_V3";
        private const string BUNDLE_FILE = "v3_bundle_scene";
        private const string BUNDLE_SCENE = "v3_bundle_scene";
        private const float INTRO_DURATION = 7f;
        private static AssetBundle _introBundle;

        // ── Сцены ────────────────────────────────────────────────────────────
        private const string TARGET_SCENE = "b3e7f2f8052488a45b35549efb98d902";
        private const string V2_TO_V1_SCENE = "36abcaae9708abc4d9e89e6ec73a2846";
        // SPLASH_SCENE перехватывается и заменяется бандлом
        private const string SPLASH_SCENE = "241a6a8caec7a13438a5ee786040de32";

        private const string REPLACEMENT_VIDEO = "0001-0250.webm";

        // ── Словари текстур ──────────────────────────────────────────────────
        internal static readonly Dictionary<string, string> V1ModelTextureMap = new Dictionary<string, string>
        {
            { "v1colour_tex",      "v3colour_tex"      },
            { "v1_wingcolour_tex", "v3_wingcolour_tex" }
        };

        internal static readonly Dictionary<string, string> SceneTextureMap = new Dictionary<string, string>
        {
            { "v2_armtex",       "v3_armtex"        },
            { "T_Feedbacker",    "T_FireArm"         },
            { "T_GreenArm",      "T_PurpleArm"       },
            { "TextmodeV1",      "TextmodeV3"        },
            { "TextmodeV1Arm1",  "TextmodeV3Arm1"    },
            { "TextmodeV1Arm2",  "TextmodeV3Arm2"    },
            { "TextmodeV1Wings", "TextmodeV3Wings"   },
            { "TextmodeCircuit", "V3TextmodeCircuit" },
            { "TextmodeLogo",    "TextmodeLogoV3"    }
        };

        internal static readonly Dictionary<string, string> TextmodeUIMap = new Dictionary<string, string>
        {
            { "TextmodeV1",      "TextmodeV3"        },
            { "TextmodeV1Arm1",  "TextmodeV3Arm1"    },
            { "TextmodeV1Arm2",  "TextmodeV3Arm2"    },
            { "TextmodeV1Wings", "TextmodeV3Wings"   },
            { "TextmodeCircuit", "V3TextmodeCircuit" },
            { "TextmodeLogo",    "TextmodeLogoV3"    }
        };

        internal static readonly Dictionary<string, string> V2ToV1TextureMap = new Dictionary<string, string>
        {
            { "v2colour_tex",        "v1colour_tex"      },
            { "v2_wingcolour_tex",   "v1_wingcolour_tex" },
            { "v2_wingcolour_tex 3", "v1_wingcolour_tex" },
            { "v2_wingcolour_tex 2", "v1_wingcolour_tex" },
            { "v2_wingcolour_tex 1", "v1_wingcolour_tex" }
        };

        internal static readonly Dictionary<string, Texture2D> LoadedTextures = new Dictionary<string, Texture2D>();

        // ────────────────────────────────────────────────────────────────────
        //  Awake
        // ────────────────────────────────────────────────────────────────────
        private void Awake()
        {
            Log = Logger;
            string modDir = Path.GetDirectoryName(Info.Location);
            TextureFolderPath = modDir;
            VideoFolderPath = modDir;

            Log.LogInfo($"[Player_V3] Texture folder: {TextureFolderPath}");

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

            Log.LogInfo("[Player_V3] Mod loaded! X=light | R=black hole | Wall=remap A/D→W/S");
        }

        // ────────────────────────────────────────────────────────────────────
        //  OnSceneLoaded
        // ────────────────────────────────────────────────────────────────────
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Log.LogInfo($"[Player_V3] Scene: {scene.name}");

            // Интро-сцену из бандла не патчим
            if (IsIntroScene(scene))
            {
                Log.LogInfo("[Player_V3] Bundle intro scene — skipping patch.");
                return;
            }

            // ── SPLASH перехватываем: заменяем бандл-сценой ─────────────────
            if (scene.name == SPLASH_SCENE)
            {
                Log.LogInfo("[Player_V3] Splash intercepted — starting bundle intro.");
                StartCoroutine(BundleIntroSequence());
                return;
            }

            StartCoroutine(ReplaceAfterFrame(scene.name));
        }

        private static bool IsIntroScene(Scene scene) =>
            scene.name == BUNDLE_SCENE ||
            Path.GetFileNameWithoutExtension(scene.path)
                .Equals(BUNDLE_SCENE, System.StringComparison.OrdinalIgnoreCase);

        // ────────────────────────────────────────────────────────────────────
        //  BundleIntroSequence
        //  Вместо SPLASH_SCENE (241a6a...) показывает v3_bundle_scene 7 сек,
        //  затем переходит на TARGET_SCENE (b3e7f2...)
        // ────────────────────────────────────────────────────────────────────
        private IEnumerator BundleIntroSequence()
        {
            string bundlePath = Path.Combine(
                Path.GetDirectoryName(Info.Location),
                BUNDLE_FOLDER,
                BUNDLE_FILE);

            if (!File.Exists(bundlePath))
            {
                Log.LogError($"[Player_V3] Bundle not found: {bundlePath}");
                Log.LogWarning("[Player_V3] Skipping intro → target scene.");
                yield return LoadSceneAsync(TARGET_SCENE);
                yield break;
            }

            // Загружаем бандл асинхронно
            Log.LogInfo($"[Player_V3] Loading bundle: {bundlePath}");
            AssetBundleCreateRequest req = AssetBundle.LoadFromFileAsync(bundlePath);
            yield return req;

            _introBundle = req.assetBundle;
            if (_introBundle == null)
            {
                Log.LogError("[Player_V3] Bundle load failed.");
                yield return LoadSceneAsync(TARGET_SCENE);
                yield break;
            }

            // Ищем сцену в бандле
            string[] scenePaths = _introBundle.GetAllScenePaths();
            if (scenePaths == null || scenePaths.Length == 0)
            {
                Log.LogError("[Player_V3] Bundle has no scenes.");
                _introBundle.Unload(false);
                _introBundle = null;
                yield return LoadSceneAsync(TARGET_SCENE);
                yield break;
            }

            string scenePath = scenePaths[0];
            foreach (string sp in scenePaths)
            {
                if (Path.GetFileNameWithoutExtension(sp)
                    .Equals(BUNDLE_SCENE, System.StringComparison.OrdinalIgnoreCase))
                { scenePath = sp; break; }
            }

            Log.LogInfo($"[Player_V3] Loading intro scene: {scenePath}");
            AsyncOperation sceneOp =
                SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single);

            if (sceneOp == null)
            {
                Log.LogError("[Player_V3] LoadSceneAsync returned null.");
                _introBundle.Unload(false);
                _introBundle = null;
                yield return LoadSceneAsync(TARGET_SCENE);
                yield break;
            }
            yield return sceneOp;

            // ── Таймер 7 секунд ───────────────────────────────────────────────
            Log.LogInfo($"[Player_V3] Intro running — {INTRO_DURATION}s...");
            yield return new WaitForSeconds(INTRO_DURATION);

            // Выгружаем и переходим
            Log.LogInfo("[Player_V3] Intro done → target scene.");
            _introBundle.Unload(false);
            _introBundle = null;

            yield return LoadSceneAsync(TARGET_SCENE);
        }

        private static IEnumerator LoadSceneAsync(string sceneName)
        {
            AsyncOperation op =
                SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (op == null)
            {
                Log.LogError(
                    $"[Player_V3] Cannot load '{sceneName}'. Add it to Build Settings.");
                yield break;
            }
            yield return op;
            Log.LogInfo($"[Player_V3] '{sceneName}' loaded.");
        }

        // ────────────────────────────────────────────────────────────────────
        //  ReplaceAfterFrame
        // ────────────────────────────────────────────────────────────────────
        private IEnumerator ReplaceAfterFrame(string sceneName)
        {
            yield return null;
            yield return null;

            SetupPlayer();
            ReplaceOnV1Model();
            HandleV1Combined();
            ReplaceSceneWide();
            ReplaceRightArmTexture();
            ReplaceUIText();
            DuplicateCoins();
            AddFireSimpleToArmRed();

            if (sceneName == V2_TO_V1_SCENE)
            {
                Log.LogInfo("[Player_V3] V2→V1 scene.");
                ReplaceV2ToV1SceneWide();
                StartCoroutine(RepeatReplaceV2ToV1());
            }

            if (sceneName == TARGET_SCENE)
            {
                Log.LogInfo("[Player_V3] Target scene.");
                ReplaceCanvasUI();
                StartCoroutine(RepeatReplaceForTargetScene());
            }
        }

        // ────────────────────────────────────────────────────────────────────
        //  SetupPlayer  — добавляет V3WallMovement
        // ────────────────────────────────────────────────────────────────────
        public static void SetupPlayer()
        {
            GameObject player =
                GameObject.FindWithTag("Player") ?? FindObject("Player");
            if (player == null)
            { Log.LogWarning("[Player_V3] Player not found."); return; }

            const float PLAYER_SCALE = 1.5f;
            const float COMBINED_SCALE = 1.3f;
            const float TOLERANCE = 0.01f;

            Transform pt = player.transform;
            if (Mathf.Abs(pt.localScale.x - PLAYER_SCALE) > TOLERANCE)
                pt.localScale *= PLAYER_SCALE;

            if (player.GetComponent<V3LightController>() == null)
                player.AddComponent<V3LightController>();
            if (player.GetComponent<V3BlackHoleSpawner>() == null)
                player.AddComponent<V3BlackHoleSpawner>();

            // ── Wall movement ──────────────────────────────────────────────
            if (player.GetComponent<V3WallMovement>() == null)
                player.AddComponent<V3WallMovement>();

            foreach (Transform child in
                player.GetComponentsInChildren<Transform>(true))
            {
                if (child.name != "v1_mdl") continue;
                if (Mathf.Abs(child.localScale.x - PLAYER_SCALE) > TOLERANCE)
                    child.localScale *= PLAYER_SCALE;
                ApplyToGameObject(child.gameObject, V1ModelTextureMap);
                break;
            }

            foreach (Transform child in
                player.GetComponentsInChildren<Transform>(true))
            {
                if (child.name != "v1_combined") continue;
                if (Mathf.Abs(child.localScale.x - COMBINED_SCALE) > TOLERANCE)
                    child.localScale *= COMBINED_SCALE;
                ApplyToGameObject(child.gameObject, SceneTextureMap);
                break;
            }
        }

        // ── FireSimple ────────────────────────────────────────────────────────
        public static void AddFireSimpleToArmRed()
        {
            GameObject armRed = GameObject.Find("Arm Red(Clone)");
            if (armRed == null)
                foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                {
                    armRed = FindInChildrenByName(root.transform, "Arm Red(Clone)");
                    if (armRed != null) break;
                }
            if (armRed == null) return;

            System.Type fireSimpleType = null;
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                fireSimpleType = asm.GetType("FireSimple");
                if (fireSimpleType != null) break;
                foreach (var t in asm.GetTypes())
                    if (t.Name == "FireSimple") { fireSimpleType = t; break; }
                if (fireSimpleType != null) break;
            }

            if (fireSimpleType == null)
            { Log.LogWarning("[Player_V3] Type 'FireSimple' not found."); return; }

            if (armRed.GetComponent(fireSimpleType) == null)
            {
                armRed.AddComponent(fireSimpleType);
                Log.LogInfo("[Player_V3] FireSimple added to 'Arm Red(Clone)'.");
            }
        }

        private static GameObject FindInChildrenByName(Transform parent, string n)
        {
            if (parent.name == n) return parent.gameObject;
            foreach (Transform c in parent)
            { var r = FindInChildrenByName(c, n); if (r != null) return r; }
            return null;
        }

        // ── Coins ─────────────────────────────────────────────────────────────
        public static void DuplicateCoins()
        {
            const int COUNT = 5;
            const float SPREAD = 1.5f;

            var allCoins = new List<GameObject>();
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go.name == "Coin(Clone)" && go.scene.IsValid())
                    allCoins.Add(go);

            if (allCoins.Count == 0)
            { Log.LogWarning("[Player_V3] No Coins found."); return; }

            int total = 0;
            foreach (var coin in allCoins)
                for (int i = 0; i < COUNT; i++)
                {
                    float a = i * (360f / COUNT) * Mathf.Deg2Rad;
                    Vector3 o = new Vector3(Mathf.Cos(a) * SPREAD, 0f, Mathf.Sin(a) * SPREAD);
                    var cl = Object.Instantiate(
                        coin, coin.transform.position + o,
                        coin.transform.rotation, coin.transform.parent);
                    cl.name = "Coin"; cl.SetActive(true); total++;
                }

            Log.LogInfo($"[Player_V3] Coins: {allCoins.Count} → +{total}.");
        }

        // ── Texture helpers ───────────────────────────────────────────────────
        public static void ReplaceV2ToV1SceneWide()
        {
            int replaced = 0;
            foreach (var rend in FindObjectsOfType<Renderer>(true))
            {
                var mats = rend.sharedMaterials; bool d = false;
                for (int i = 0; i < mats.Length; i++)
                    if (ApplyToMaterial(mats[i], V2ToV1TextureMap)) { replaced++; d = true; }
                if (d) rend.sharedMaterials = mats;
            }
            Log.LogInfo($"[Player_V3] V2→V1: {replaced}");
        }

        public static void ReplaceRightArmTexture()
        {
            if (!LoadedTextures.TryGetValue("T_Feedbacker", out Texture2D tex))
            { Log.LogWarning("[Player_V3] T_FireArm not cached."); return; }

            GameObject ra = FindObject("RightArm");
            if (ra == null) return;

            int replaced = 0;
            foreach (var rend in ra.GetComponentsInChildren<Renderer>(true))
            {
                var mats = rend.sharedMaterials; bool d = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    foreach (string p in mats[i].GetTexturePropertyNames())
                    {
                        if (mats[i].GetTexture(p) == null) continue;
                        mats[i].SetTexture(p, tex); d = true; replaced++;
                    }
                }
                if (d) rend.sharedMaterials = mats;
            }
            Log.LogInfo($"[Player_V3] RightArm: {replaced}");
        }

        private static readonly (string from, string to)[] UITextReplacements =
        {
            ("Find\u00a0a weapon", "liquidation V1"),
            ("Find a weapon",      "liquidation V1"),
            ("Find\u0430 weapon",  "liquidation V1"),
            ("Hell is Full",       "THIS SCUM WILL BE DESTROYED"),
            ("(2112.08.06)",       "(2112.09.13)"),
            ("V1", "V3"), ("v1", "v3"),
        };

        public static void ReplaceUIText()
        {
            int r = 0;
            foreach (var l in FindObjectsOfType<Text>(true))
            { string u = ApplyTextReplacements(l.text); if (u != l.text) { l.text = u; r++; } }
            foreach (var t in FindObjectsOfType<TMP_Text>(true))
            { string u = ApplyTextReplacements(t.text); if (u != t.text) { t.text = u; r++; } }
            Log.LogInfo($"[Player_V3] UI texts: {r}");
        }

        private static string ApplyTextReplacements(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            foreach (var (from, to) in UITextReplacements) text = text.Replace(from, to);
            return text;
        }

        public static void ReplaceCanvasUI()
        {
            int r = 0;
            foreach (var ri in FindObjectsOfType<RawImage>(true))
            {
                if (ri.texture == null) continue;
                if (TextmodeUIMap.ContainsKey(ri.texture.name) &&
                    LoadedTextures.TryGetValue(ri.texture.name, out Texture2D rep))
                { ri.texture = rep; r++; }
            }
            foreach (var img in FindObjectsOfType<Image>(true))
            {
                if (img.sprite?.texture == null) continue;
                if (TextmodeUIMap.ContainsKey(img.sprite.texture.name) &&
                    LoadedTextures.TryGetValue(img.sprite.texture.name, out Texture2D rep))
                {
                    img.sprite = Sprite.Create(rep,
                        new Rect(0, 0, rep.width, rep.height), new Vector2(0.5f, 0.5f));
                    r++;
                }
            }
            Log.LogInfo($"[Player_V3] Canvas UI: {r}");
        }

        public static void ReplaceOnV1Model()
        {
            GameObject g = FindObject("v1_mdl");
            if (g == null) { Log.LogWarning("[Player_V3] v1_mdl not found."); return; }
            ApplyToGameObject(g, V1ModelTextureMap);
        }

        public static void HandleV1Combined()
        {
            GameObject g = FindObject("v1_combined");
            if (g == null) { Log.LogWarning("[Player_V3] v1_combined not found."); return; }
            ApplyToGameObject(g, SceneTextureMap);
        }

        public static void ReplaceSceneWide()
        {
            int r = 0;
            foreach (var rend in FindObjectsOfType<Renderer>(true))
            {
                var mats = rend.sharedMaterials; bool d = false;
                for (int i = 0; i < mats.Length; i++)
                    if (ApplyToMaterial(mats[i], SceneTextureMap)) { r++; d = true; }
                if (d) rend.sharedMaterials = mats;
            }
            Log.LogInfo($"[Player_V3] Scene-wide: {r}");
        }

        public static void ApplyToGameObject(
            GameObject go, Dictionary<string, string> map)
        {
            int r = 0;
            foreach (var rend in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = rend.sharedMaterials; bool d = false;
                for (int i = 0; i < mats.Length; i++)
                    if (ApplyToMaterial(mats[i], map)) { r++; d = true; }
                if (d) rend.sharedMaterials = mats;
            }
            Log.LogInfo($"[Player_V3] [{go.name}]: {r}");
        }

        public static bool ApplyToMaterial(
            Material mat, Dictionary<string, string> map)
        {
            if (mat == null) return false;
            bool changed = false;
            foreach (string prop in mat.GetTexturePropertyNames())
            {
                Texture cur = mat.GetTexture(prop);
                if (cur == null) continue;
                if (map.ContainsKey(cur.name) &&
                    LoadedTextures.TryGetValue(cur.name, out Texture2D rep))
                { mat.SetTexture(prop, rep); changed = true; }
            }
            return changed;
        }

        public static GameObject FindObject(string targetName)
        {
            GameObject f = GameObject.Find(targetName);
            if (f != null) return f;
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            { var r = FindInChildren(root.transform, targetName); if (r != null) return r; }
            return null;
        }

        private static GameObject FindInChildren(Transform parent, string n)
        {
            if (parent.name == n) return parent.gameObject;
            foreach (Transform c in parent)
            { var r = FindInChildren(c, n); if (r != null) return r; }
            return null;
        }

        private static void PreloadTextures(Dictionary<string, string> map)
        {
            string[] exts = { ".png", ".jpg", ".jpeg" };
            foreach (var pair in map)
            {
                if (LoadedTextures.ContainsKey(pair.Key)) continue;
                bool found = false;
                foreach (string ext in exts)
                {
                    string path = Path.Combine(TextureFolderPath, pair.Value + ext);
                    if (!File.Exists(path)) continue;
                    Texture2D tex = LoadPNG(path);
                    if (tex != null)
                    {
                        tex.name = pair.Value;
                        LoadedTextures[pair.Key] = tex;
                        Log.LogInfo($"[Player_V3] OK {pair.Key} → {pair.Value}{ext}");
                        found = true;
                    }
                    break;
                }
                if (!found) Log.LogWarning($"[Player_V3] MISS {pair.Value}");
            }
        }

        public static Texture2D LoadPNG(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (tex.LoadImage(data)) { tex.Apply(true, false); return tex; }
            Log.LogError($"[Player_V3] Decode failed: {path}");
            return null;
        }

        private IEnumerator RepeatReplaceForTargetScene()
        {
            yield return new WaitForSeconds(0.5f);
            SetupPlayer(); ReplaceSceneWide(); ReplaceCanvasUI();
            ReplaceRightArmTexture(); ReplaceUIText(); AddFireSimpleToArmRed();
            yield return new WaitForSeconds(0.5f);
            SetupPlayer(); ReplaceSceneWide(); ReplaceCanvasUI();
            ReplaceRightArmTexture(); ReplaceUIText(); AddFireSimpleToArmRed();
            yield return new WaitForSeconds(1.0f);
            SetupPlayer(); ReplaceSceneWide(); ReplaceCanvasUI();
            ReplaceRightArmTexture(); ReplaceUIText(); AddFireSimpleToArmRed();
        }

        private IEnumerator RepeatReplaceV2ToV1()
        {
            yield return new WaitForSeconds(0.5f);
            ReplaceV2ToV1SceneWide(); ReplaceRightArmTexture();
            ReplaceUIText(); AddFireSimpleToArmRed();
            yield return new WaitForSeconds(0.5f);
            ReplaceV2ToV1SceneWide(); ReplaceRightArmTexture();
            ReplaceUIText(); AddFireSimpleToArmRed();
            yield return new WaitForSeconds(1.0f);
            ReplaceV2ToV1SceneWide(); ReplaceRightArmTexture();
            ReplaceUIText(); AddFireSimpleToArmRed();
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  V3WallMovement
    //  При касании объекта с тегом "Wall":
    //    A → движение вперёд (как W)
    //    D → движение назад  (как S)
    //
    //  Приоритет управления: CharacterController → Rigidbody → Transform
    // ══════════════════════════════════════════════════════════════════════════
    [RequireComponent(typeof(Collider))]
    public class V3WallMovement : MonoBehaviour
    {
        private const float SPEED = 8f;

        // Счётчик активных касаний со стенами
        private int _wallContacts = 0;
        private bool OnWall => _wallContacts > 0;

        private CharacterController _cc;
        private Rigidbody _rb;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _rb = GetComponent<Rigidbody>();
        }

        // ── Отслеживание контактов ───────────────────────────────────────────
        private void OnCollisionEnter(Collision col)
        {
            if (col.gameObject.CompareTag("Wall"))
            {
                _wallContacts++;
                Plugin.Log.LogInfo(
                    $"[V3WallMovement] Wall contact enter. Total: {_wallContacts}");
            }
        }

        private void OnCollisionExit(Collision col)
        {
            if (col.gameObject.CompareTag("Wall"))
            {
                _wallContacts = Mathf.Max(0, _wallContacts - 1);
                Plugin.Log.LogInfo(
                    $"[V3WallMovement] Wall contact exit. Total: {_wallContacts}");
            }
        }

        // ── Движение ─────────────────────────────────────────────────────────
        private void Update()
        {
            if (!OnWall) return;

            bool pressA = Input.GetKey(KeyCode.A);
            bool pressD = Input.GetKey(KeyCode.D);
            if (!pressA && !pressD) return;

            // A → вперёд (+1), D → назад (-1)
            float sign = pressA ? 1f : -1f;

            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.001f) return;
            fwd.Normalize();

            Vector3 delta = fwd * (sign * SPEED * Time.deltaTime);

            if (_cc != null && _cc.enabled)
            {
                // CharacterController — предпочтительный способ
                _cc.Move(delta);
            }
            else if (_rb != null && !_rb.isKinematic)
            {
                // Rigidbody — задаём velocity (сохраняем вертикаль)
                _rb.velocity = new Vector3(
                    delta.x / Time.deltaTime,
                    _rb.velocity.y,
                    delta.z / Time.deltaTime);
            }
            else
            {
                // Fallback — прямое смещение
                transform.position += delta;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  V3LightController — клавиша X
    // ══════════════════════════════════════════════════════════════════════════
    public class V3LightController : MonoBehaviour
    {
        private Light _pointLight;

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.X)) return;
            if (_pointLight == null) CreateLight(); else ToggleLight();
        }

        private void CreateLight()
        {
            Transform v1mdl = null;
            foreach (Transform c in GetComponentsInChildren<Transform>(true))
                if (c.name == "v1_mdl") { v1mdl = c; break; }

            Transform parent = v1mdl != null ? v1mdl : transform;
            var go = new GameObject("Player_V3_PointLight");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;

            _pointLight = go.AddComponent<Light>();
            _pointLight.type = LightType.Point;
            _pointLight.color = Color.green;
            _pointLight.range = 200f;
            _pointLight.renderMode = LightRenderMode.ForcePixel;
            _pointLight.enabled = true;

            Plugin.Log.LogInfo($"[Player_V3] Light on '{parent.name}'.");
        }

        private void ToggleLight()
        {
            _pointLight.enabled = !_pointLight.enabled;
            Plugin.Log.LogInfo(
                $"[Player_V3] Light: {(_pointLight.enabled ? "ON" : "OFF")}");
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  V3BlackHoleSpawner — клавиша R (требует Hook Arm на сцене)
    // ══════════════════════════════════════════════════════════════════════════
    public class V3BlackHoleSpawner : MonoBehaviour
    {
        private static readonly string[] Candidates =
            { "Black Hole Projectile" };
        private static readonly string[] Prefixes =
            { "", "Prefabs/", "Projectiles/", "Prefabs/Projectiles/" };

        private static GameObject _prefab;

        private const float COOLDOWN = 0.5f;
        private const float SPEED = 30f;
        private float _lastFire = -999f;

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.R)) return;
            if (Time.time - _lastFire < COOLDOWN) return;
            _lastFire = Time.time;
            TrySpawn();
        }

        private void TrySpawn()
        {
            if (!HookArmPresent())
            { Plugin.Log.LogWarning("[V3BlackHole] Hook Arm missing — blocked."); return; }

            if (_prefab == null) _prefab = FindPrefab();
            if (_prefab == null)
            { Plugin.Log.LogWarning("[V3BlackHole] Prefab not found."); return; }

            Camera cam = Camera.main;
            Vector3 fwd = cam != null ? cam.transform.forward : transform.forward;
            Vector3 pos = cam != null
                ? cam.transform.position + fwd * 2f
                : transform.position + fwd * 2f;

            var inst = Instantiate(_prefab, pos, Quaternion.LookRotation(fwd));
            var rb = inst.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic) rb.velocity = fwd * SPEED;

            foreach (var comp in inst.GetComponents<MonoBehaviour>())
            {
                if (comp == null) continue;
                var f = comp.GetType().GetField("owner",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                if (f == null) continue;
                f.SetValue(comp, 1);
                break;
            }

            Plugin.Log.LogInfo($"[V3BlackHole] Spawned at {pos}");
        }

        private static bool HookArmPresent()
        {
            if (GameObject.Find("Hook Arm") != null) return true;
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go.name == "Hook Arm" && go.scene.IsValid()) return true;
            return false;
        }

        private static GameObject FindPrefab()
        {
            foreach (string p in Prefixes)
                foreach (string n in Candidates)
                {
                    var go = Resources.Load<GameObject>(p + n);
                    if (go != null) return go;
                }

            foreach (string n in Candidates)
            {
                var live = GameObject.Find(n);
                if (live == null) continue;
                var tpl = Instantiate(live);
                tpl.name = n; tpl.SetActive(false); DontDestroyOnLoad(tpl);
                return tpl;
            }

            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                foreach (string n in Candidates)
                    if (string.Equals(go.name, n,
                        System.StringComparison.OrdinalIgnoreCase)) return go;

            return null;
        }
    }

    // ── Metadata ──────────────────────────────────────────────────────────────
    internal static class PluginInfo
    {
        internal const string PLUGIN_GUID = "com.lazurut.ultrakill.player_v3";
        internal const string PLUGIN_NAME = "PLAYER V3";
        internal const string PLUGIN_VERSION = "10.0.4";
    }
}