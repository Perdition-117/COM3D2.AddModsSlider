using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using BepInEx;
using I2.Loc;

using CM3D2.ExternalSaveData.Managed;
using CM3D2.ExternalPreset.Managed;

namespace CM3D2.AddModsSlider.Plugin {
    //[PluginFilter("CM3D2x64"), PluginFilter("CM3D2x86"), PluginFilter("CM3D2VRx64")]
	[BepInPlugin("CM3D2.AddModsSlider", "CM3D2 AddModsSlider", "0.1.3.6")]
	public class AddModsSlider : BaseUnityPlugin {

        #region Constants

        public const string PluginName = "AddModsSlider";
        public const string Version = "0.1.3.6";

        private readonly string LogLabel = AddModsSlider.PluginName + " : ";

        private readonly float TimePerInit = 0.10f;

        private readonly int UIRootWidth = 1920; // GemaObject.Find("UI Root").GetComponent<UIRoot>().manualWidth;
        private readonly int UIRootHeight = 1080; // GemaObject.Find("UI Root").GetComponent<UIRoot>().manualHeight;
        private readonly int ScrollViewWidth = 550;
        private readonly int ScrollViewHeight = 860;

        #endregion



        #region Variables

        private bool xmlLoad = false;
        private bool visible = false;
        private bool bInitCompleted = false;

        private ModsParam mp;
        private Dictionary<string, Dictionary<string, float>> undoValue = new();

        private Maid maid;
        private GameObject goAMSPanel;
        private GameObject goScrollView;
        private GameObject goScrollViewTable;
        private UICamera uiCamara;
        private UIPanel uiAMSPanel;
        private UIPanel uiScrollPanel;
        private UIScrollView uiScrollView;
        private UIScrollBar uiScrollBar;
        private UITable uiTable;
        private Font font;
        private Dictionary<string, Transform> trModUnit = new();
        private Dictionary<string, Dictionary<string, UILabel>> uiValueLable = new();

        static private List<ExternalModsParam> externalModsParamList = new();

        #endregion



        #region Nested classes

        private class ModsParam {
            private readonly string LogLabel = AddModsSlider.PluginName + " : ";

            public readonly string DefMatchPattern = @"([-+]?[0-9]*\.?[0-9]+)";
            public readonly string XmlFileName = Path.Combine(Paths.ConfigPath, "ModsParam.xml");


            public string XmlFormat;
            public List<string> sKey = new();

            public Dictionary<string, bool> bEnabled = new();
            public Dictionary<string, string> sDescription = new();
            public Dictionary<string, string> sType = new();
            public Dictionary<string, bool> bOnWideSlider = new();
            public Dictionary<string, bool> bVisible = new();

            public Dictionary<string, string[]> sPropName = new();
            public Dictionary<string, Dictionary<string, float>> fValue = new();
            public Dictionary<string, Dictionary<string, float>> fVmin = new();
            public Dictionary<string, Dictionary<string, float>> fVmax = new();
            public Dictionary<string, Dictionary<string, float>> fVdef = new();
            public Dictionary<string, Dictionary<string, string>> sVType = new();
            public Dictionary<string, Dictionary<string, string>> sLabel = new();
            public Dictionary<string, Dictionary<string, string>> sMatchPattern = new();
            public Dictionary<string, Dictionary<string, bool>> bVVisible = new();

            public int KeyCount => sKey.Count;
            public int ValCount(string key) => sPropName[key].Length;

            //--------

            public ModsParam() { }

            public bool Init() {
                if (!loadModsParamXML()) {
                    Debug.LogError(LogLabel + "loadModsParamXML() failed.");
                    return false;
                }
                ApplyExternalModsParam();
                foreach (var key in sKey) CheckWS(key);

                return true;
            }

            public bool CheckWS(string key) {
                return !bOnWideSlider[key] || (sKey.Contains("WIDESLIDER") && bEnabled["WIDESLIDER"]);
            }

            public bool IsToggle(string key) {
                return (sType[key].Contains("toggle")) ? true : false;
            }

            public bool IsSlider(string key) {
                return (sType[key].Contains("slider")) ? true : false;
            }

            //--------

            private bool loadModsParamXML() {
                if (!File.Exists(XmlFileName)) {
                    Debug.LogError($"{LogLabel}\"{XmlFileName}\" does not exist.");
                    return false;
                }

				var doc = new XmlDocument();
                doc.Load(XmlFileName);

				var mods = (XmlNode)doc.DocumentElement;
                XmlFormat = ((XmlElement)mods).GetAttribute("format");
                if (XmlFormat != "1.2" && XmlFormat != "1.21") {
                    Debug.LogError($"{LogLabel}{AddModsSlider.Version} requires fomart=\"1.2\" or \"1.21\" of ModsParam.xml.");
                    return false;
                }

				var modNodeS = mods.SelectNodes("/mods/mod");
                if (!(modNodeS.Count > 0)) {
                    Debug.LogError($"{LogLabel} \"{XmlFileName}\" has no <mod>elements.");
                    return false;
                }

                sKey.Clear();

                foreach (XmlNode modNode in modNodeS) {
					// mod属性
					var key = ((XmlElement)modNode).GetAttribute("id");
                    if (key != "" && !sKey.Contains(key)) sKey.Add(key);
                    else continue;

					var b = false;
                    bEnabled[key] = false;
                    sDescription[key] = ((XmlElement)modNode).GetAttribute("description");
                    bOnWideSlider[key] = (bool.TryParse(((XmlElement)modNode).GetAttribute("on_wideslider"), out b)) ? b : false;
                    bVisible[key] = (bool.TryParse(((XmlElement)modNode).GetAttribute("visible"), out b)) ? b : true;

                    sType[key] = ((XmlElement)modNode).GetAttribute("type");
                    switch (sType[key]) {
                        case "toggle": break;
                        case "toggle,slider": break;
                        default: sType[key] = "slider"; break;
                    }

                    if (!IsSlider(key)) continue;

					var valueNodeS = ((XmlElement)modNode).GetElementsByTagName("value");
                    if (!(valueNodeS.Count > 0)) continue;

                    sPropName[key] = new string[valueNodeS.Count];
                    fValue[key] = new();
                    fVmin[key] = new();
                    fVmax[key] = new();
                    fVdef[key] = new();
                    sVType[key] = new();
                    sLabel[key] = new();
                    sMatchPattern[key] = new();
                    bVVisible[key] = new();

					// value属性
					var j = 0;
                    foreach (XmlNode valueNode in valueNodeS) {
						var x = 0f;

                        string prop = ((XmlElement)valueNode).GetAttribute("prop_name");
                        if (prop != "" && Array.IndexOf(sPropName[key], prop) < 0) {
                            sPropName[key][j] = prop;
                        } else {
                            sKey.Remove(key);
                            break;
                        }

                        sVType[key][prop] = ((XmlElement)valueNode).GetAttribute("type");
                        switch (sVType[key][prop]) {
                            case "num": break;
                            case "scale": break;
                            case "int": break;
                            default: sVType[key][prop] = "num"; break;
                        }

                        fVmin[key][prop] = float.TryParse(((XmlElement)valueNode).GetAttribute("min"), out x) ? x : 0f;
                        fVmax[key][prop] = float.TryParse(((XmlElement)valueNode).GetAttribute("max"), out x) ? x : 0f;
                        fVdef[key][prop] = float.TryParse(((XmlElement)valueNode).GetAttribute("default"), out x) ? x : float.NaN;
                        if (float.IsNaN(fVdef[key][prop])) {
							fVdef[key][prop] = sVType[key][prop] switch {
								"num" => 0f,
								"scale" => 1f,
								"int" => 0f,
								_ => 0f,
							};
						}

                        fValue[key][prop] = fVdef[key][prop];

                        sLabel[key][prop] = ((XmlElement)valueNode).GetAttribute("label");
                        sMatchPattern[key][prop] = ((XmlElement)valueNode).GetAttribute("match_pattern");
                        bVVisible[key][prop] = (bool.TryParse(((XmlElement)valueNode).GetAttribute("visible"), out b)) ? b : true;

                        j++;
                    }
                    if (j == 0) sKey.Remove(key);
                }

                return true;
            }

            private void ApplyExternalModsParam() {
                foreach (var emp in externalModsParamList) {
					var key = emp.sKey;
                    if (string.IsNullOrEmpty(key) || sKey.Contains(key)) return;
                    if (!string.IsNullOrEmpty(emp.sInsertID) && sKey.IndexOf(emp.sInsertID) != -1) {
                        sKey.Insert(sKey.IndexOf(emp.sInsertID), emp.sKey);
                    } else {
                        sKey.Add(key);
                    }
                    bEnabled[key] = false;
                    sDescription[key] = emp.sDescription;
                    bOnWideSlider[key] = emp.bOnWideSlider;
                    sType[key] = emp.sType;
                    bVisible[key] = true;

                    if (!IsSlider(key)) continue;

                    if (emp.lValueList == null || emp.lValueList.Count == 0) {
                        sKey.Remove(key);
                        continue;
                    }
                    sPropName[key] = new string[emp.lValueList.Count];
                    fValue[key] = new();
                    fVmin[key] = new();
                    fVmax[key] = new();
                    fVdef[key] = new();
                    sVType[key] = new();
                    sLabel[key] = new();
                    bVVisible[key] = new();
					var i = 0;
                    foreach (var empValue in emp.lValueList) {
						var prop = empValue.sPropName;
                        if (string.IsNullOrEmpty(prop) || Array.IndexOf(sPropName[key], prop) >= 0) {
                            sKey.Remove(key);
                            break;
                        }

                        sPropName[key][i] = prop;
                        i++;
                        sVType[key][prop] = empValue.sType;
                        fVmin[key][prop] = empValue.fMin;
                        fVmax[key][prop] = empValue.fMax;
                        fVdef[key][prop] = empValue.fDef;
                        fValue[key][prop] = empValue.fDef;
                        sLabel[key][prop] = empValue.sLabel;
                        bVVisible[key][prop] = true;
                    }
                }
            }
        }

        // insertIDに指定したIDの前に挿入する。からの場合は最後に追加
        public class ExternalModsParam {
            public readonly string sKey;
            public readonly string sDescription;
            public readonly bool bOnWideSlider;
            public readonly string sType;
            public readonly string sInsertID;

            public readonly List<ExternalModsParamValue> lValueList;

            public ExternalModsParam(string id, string description, bool onWideSlider = false, string type = "toggle", string insertID = "", List<ExternalModsParamValue> valueList = null) {
                sKey = id;
                sDescription = description;
                bOnWideSlider = onWideSlider;
                sType = type;
                sInsertID = insertID;
                lValueList = valueList;
            }
        }

        public class ExternalModsParamValue {
            public readonly string sPropName;
            public readonly string sLabel;
            public readonly string sType;
            public readonly float fMin;
            public readonly float fMax;
            public readonly float fDef;

            public ExternalModsParamValue(string propName, string label, string type = "num", float min = 0f, float max = 0f, float def = 0f) {
                sPropName = propName;
                sLabel = label;
                sType = type;
                fMin = min;
                fMax = max;
                fDef = def;
            }
        }

        #endregion



        #region MonoBehaviour methods
        void Start() {
            SceneManager.sceneLoaded += OnSceneLoaded;

            ExPreset.loadNotify.AddListener(syncExSaveDatatoSlider);
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
            if (scene.name == "SceneTitle") {
                font = GameObject.Find("SystemUI Root").GetComponentsInChildren<UILabel>()[0].trueTypeFont;
            } else if (scene.name == "SceneEdit") {
                mp = new();
                if (xmlLoad = mp.Init()) StartCoroutine(initCoroutine());
            } else {
                finalize();
            }
        }

        public void Update() {
            if (SceneManager.GetActiveScene().name == "SceneEdit" && bInitCompleted) {
                if (Input.GetKeyDown(KeyCode.F5)) {
                    goAMSPanel.SetActive(visible = !visible);
                    //WriteTrans("UI Root");
                }

            }
        }

        #endregion



        #region Callbacks

        public void OnClickHeaderButton() {
            try {
				var key = getTag(UIButton.current, 1);
				var b = false;

                if (mp.IsToggle(key)) {
                    b = !mp.bEnabled[key];
                    mp.bEnabled[key] = b;
                    setExSaveData(key);

                    notifyMaidVoicePitchOnChange();

                    // WIDESLIDER有効化/無効化に合わせて、依存項目UIを表示/非表示
                    if (key == "WIDESLIDER") toggleActiveOnWideSlider();
                }

                if (mp.IsSlider(key)) {
                    if (!mp.IsToggle(key)) b = !(UIButton.current.defaultColor.a == 1f);
                    setSliderVisible(key, b);
                }

                setButtonColor(UIButton.current, b);

            } catch (Exception ex) { Debug.Log($"{LogLabel}OnClickToggleHeader() {ex}"); return; }
        }

        public void OnClickUndoAll() {
            try {
                foreach (var key in mp.sKey) {
                    if (mp.IsToggle(key)) {
                        mp.bEnabled[key] = (undoValue[key]["enable"] == 1f);
                        setExSaveData(key);
                        notifyMaidVoicePitchOnChange();
                        setButtonColor(key, mp.bEnabled[key]);
                    }

                    if (mp.IsSlider(key)) {
                        undoSliderValue(key);
                        setExSaveData(key);

                        if (mp.IsToggle(key)) {
                            setSliderVisible(key, mp.bEnabled[key]);
                        }
                    }
                }
            } catch (Exception ex) { Debug.Log($"{LogLabel}OnClickUndoAll() {ex}"); return; }
        }

        public void OnClickUndoButton() {
			var key = getTag(UIButton.current, 1);
            undoSliderValue(key);
            setExSaveData(key);
        }

        public void OnClickResetAll() {
            try {
                foreach (var key in mp.sKey) {
                    if (mp.IsToggle(key)) {
                        mp.bEnabled[key] = false;
                        setExSaveData(key);
                        notifyMaidVoicePitchOnChange();
                        setButtonColor(key, mp.bEnabled[key]);
                    }

                    if (mp.IsSlider(key)) {
                        resetSliderValue(key);
                        setExSaveData(key);

                        if (mp.IsToggle(key)) {
                            setSliderVisible(key, mp.bEnabled[key]);
                        }
                    }
                }
            } catch (Exception ex) { Debug.Log($"{LogLabel}OnClickResetAll() {ex}"); return; }
        }

        public void OnClickResetButton() {
			var key = getTag(UIButton.current, 1);
            resetSliderValue(key);
            setExSaveData(key);
        }

        public void OnChangeSlider() {
            try {
				var key = getTag(UIProgressBar.current, 1);
				var prop = getTag(UIProgressBar.current, 2);
				var value = codecSliderValue(key, prop, UIProgressBar.current.value);
				var vType = mp.sVType[key][prop];

                uiValueLable[key][prop].text = $"{value:F2}";
                uiValueLable[key][prop].gameObject.GetComponent<UIInput>().value = uiValueLable[key][prop].text;
                mp.fValue[key][prop] = value;

                setExSaveData(key, prop);

                notifyMaidVoicePitchOnChange();

                //Debug.Log(key +":"+ prop +":"+ value);
            } catch (Exception ex) { Debug.Log($"{LogLabel}OnChangeSlider() {ex}"); return; }
        }

        public void OnSubmitSliderValueInput() {
            try {
				var key = getTag(UIInput.current, 1);
				var prop = getTag(UIInput.current, 2);
                UISlider slider = null;

                foreach (Transform t in UIInput.current.transform.parent.parent) {
                    if (getTag(t, 0) == "Slider") slider = t.GetComponent<UISlider>();
                }

                if (float.TryParse(UIInput.current.value, out var value)) {
                    mp.fValue[key][prop] = value;
                    slider.value = codecSliderValue(key, prop);
                    UIInput.current.value = codecSliderValue(key, prop, slider.value).ToString("F2");
                    uiValueLable[key][prop].text = UIInput.current.value;
                }
            } catch (Exception ex) { Debug.Log($"{LogLabel}OnSubmitSliderValueInput() {ex}"); return; }
        }

        #endregion



        #region Private methods

        private IEnumerator initCoroutine() {
            while (!(bInitCompleted = initialize())) yield return new WaitForSeconds(TimePerInit);
            Debug.Log(LogLabel + "Initialization complete.");
        }

        private bool initialize() {
            try {

                maid = GameMain.Instance.CharacterMgr.GetMaid(0);
                if (maid == null) return false;

				var uiAtlasSceneEdit = FindAtlas("AtlasSceneEdit");
				var uiAtlasDialog = FindAtlas("SystemDialog");

				var goUIRoot = GameObject.Find("UI Root");
				var cameraObject = GameObject.Find("/UI Root/Camera");
				var cameraComponent = cameraObject.GetComponent<Camera>();
                uiCamara = cameraObject.GetComponent<UICamera>();

				#region createSlider

				// スライダー作成
				var goTestSliderUnit = new GameObject("TestSliderUnit");
                SetChild(goUIRoot, goTestSliderUnit);
                {
					var uiTestSliderUnitFrame = goTestSliderUnit.AddComponent<UISprite>();
                    uiTestSliderUnitFrame.atlas = uiAtlasSceneEdit;
                    uiTestSliderUnitFrame.spriteName = "cm3d2_edit_slidertitleframe";
                    uiTestSliderUnitFrame.type = UIBasicSprite.Type.Sliced;
                    uiTestSliderUnitFrame.SetDimensions(500, 50);

					// スライダー作成
					var uiTestSlider = NGUITools.AddChild<UISlider>(goTestSliderUnit);
					var uiTestSliderRail = uiTestSlider.gameObject.AddComponent<UISprite>();
                    uiTestSliderRail.name = "Slider";
                    uiTestSliderRail.atlas = uiAtlasSceneEdit;
                    uiTestSliderRail.spriteName = "cm3d2_edit_slideberrail";
                    uiTestSliderRail.type = UIBasicSprite.Type.Sliced;
                    uiTestSliderRail.SetDimensions(250, 5);

					var uiTestSliderBar = NGUITools.AddChild<UIWidget>(uiTestSlider.gameObject);
                    uiTestSliderBar.name = "DummyBar";
                    uiTestSliderBar.width = uiTestSliderRail.width;

					var uiTestSliderThumb = NGUITools.AddChild<UISprite>(uiTestSlider.gameObject);
                    uiTestSliderThumb.name = "Thumb";
                    uiTestSliderThumb.depth = uiTestSliderRail.depth + 1;
                    uiTestSliderThumb.atlas = uiAtlasSceneEdit;
                    uiTestSliderThumb.spriteName = "cm3d2_edit_slidercursor";
                    uiTestSliderThumb.type = UIBasicSprite.Type.Sliced;
                    uiTestSliderThumb.SetDimensions(25, 25);
                    uiTestSliderThumb.gameObject.AddComponent<BoxCollider>();

                    uiTestSlider.backgroundWidget = uiTestSliderRail;
                    uiTestSlider.foregroundWidget = uiTestSliderBar;
                    uiTestSlider.thumb = uiTestSliderThumb.gameObject.transform;
                    uiTestSlider.value = 0.5f;
                    uiTestSlider.gameObject.AddComponent<BoxCollider>();
                    uiTestSlider.transform.localPosition = new(100f, 0f, 0f);

                    NGUITools.UpdateWidgetCollider(uiTestSlider.gameObject);
                    NGUITools.UpdateWidgetCollider(uiTestSliderThumb.gameObject);

					// スライダーラベル作成
					var uiTestSliderLabel = NGUITools.AddChild<UILabel>(goTestSliderUnit);
                    uiTestSliderLabel.name = "Label";
                    uiTestSliderLabel.trueTypeFont = font;
                    uiTestSliderLabel.fontSize = 20;
                    uiTestSliderLabel.text = "テストスライダー";
                    uiTestSliderLabel.width = 110;
                    uiTestSliderLabel.overflowMethod = UILabel.Overflow.ShrinkContent;

                    uiTestSliderLabel.transform.localPosition = new(-190f, 0f, 0f);

					// 値ラベル・インプット作成
					var uiTestSliderValueBase = NGUITools.AddChild<UISprite>(goTestSliderUnit);
                    uiTestSliderValueBase.name = "ValueBase";
                    uiTestSliderValueBase.atlas = uiAtlasSceneEdit;
                    uiTestSliderValueBase.spriteName = "cm3d2_edit_slidernumberframe";
                    uiTestSliderValueBase.type = UIBasicSprite.Type.Sliced;
                    uiTestSliderValueBase.SetDimensions(80, 35);
                    uiTestSliderValueBase.transform.localPosition = new(-90f, 0f, 0f);

					var uiTestSliderValueLabel = NGUITools.AddChild<UILabel>(uiTestSliderValueBase.gameObject);
                    uiTestSliderValueLabel.name = "Value";
                    uiTestSliderValueLabel.depth = uiTestSliderValueBase.depth + 1;
                    uiTestSliderValueLabel.width = uiTestSliderValueBase.width;
                    uiTestSliderValueLabel.trueTypeFont = font;
                    uiTestSliderValueLabel.fontSize = 20;
                    uiTestSliderValueLabel.text = "0.00";
                    uiTestSliderValueLabel.color = Color.black;

					var uiTestSliderValueInput = uiTestSliderValueLabel.gameObject.AddComponent<UIInput>();
                    uiTestSliderValueInput.label = uiTestSliderValueLabel;
                    uiTestSliderValueInput.onReturnKey = UIInput.OnReturnKey.Submit;
                    uiTestSliderValueInput.validation = UIInput.Validation.Float;
                    uiTestSliderValueInput.activeTextColor = Color.black;
                    uiTestSliderValueInput.caretColor = new(0.1f, 0.1f, 0.3f, 1f);
                    uiTestSliderValueInput.selectionColor = new(0.3f, 0.3f, 0.6f, 0.8f);
                    //EventDelegate.Add(uiTestSliderValueInput.onSubmit, new EventDelegate.Callback(this.OnSubmitSliderValueInput));

                    uiTestSliderValueInput.gameObject.AddComponent<BoxCollider>();
                    NGUITools.UpdateWidgetCollider(uiTestSliderValueInput.gameObject);
                }
                goTestSliderUnit.SetActive(false);

				#endregion


				// ボタンはgoProfileTabをコピー
				var goProfileTabCopy = UnityEngine.Object.Instantiate(FindChild(goUIRoot.transform.Find("ProfilePanel").Find("Comment").gameObject, "ProfileTab"));
                EventDelegate.Remove(goProfileTabCopy.GetComponent<UIButton>().onClick, new EventDelegate.Callback(ProfileMgr.Instance.ChangeCommentTab));
                goProfileTabCopy.SetActive(false);


				#region createPanel

				// ModsSliderPanel作成
				var originAMSPanel = new Vector3(UIRootWidth / 2f - 15f - ScrollViewWidth / 2f - 50f, 40f, 0f);
				var systemUnitHeight = 30;

                // 親Panel
                uiAMSPanel = NGUITools.AddChild<UIPanel>(goUIRoot);
                uiAMSPanel.name = "ModsSliderPanel";
                uiAMSPanel.transform.localPosition = originAMSPanel;
                goAMSPanel = uiAMSPanel.gameObject;

				// 背景
				var uiBGSprite = NGUITools.AddChild<UISprite>(goAMSPanel);
                uiBGSprite.name = "BG";
                uiBGSprite.atlas = uiAtlasSceneEdit;
                uiBGSprite.spriteName = "cm3d2_edit_window_l";
                uiBGSprite.type = UIBasicSprite.Type.Sliced;
                uiBGSprite.SetDimensions(ScrollViewWidth, ScrollViewHeight);

                // ScrollViewPanel
                uiScrollPanel = NGUITools.AddChild<UIPanel>(goAMSPanel);
                uiScrollPanel.name = "ScrollView";
                uiScrollPanel.sortingOrder = uiAMSPanel.sortingOrder + 1;
                uiScrollPanel.clipping = UIDrawCall.Clipping.SoftClip;
                uiScrollPanel.SetRect(0f, 0f, uiBGSprite.width, uiBGSprite.height - 110 - systemUnitHeight);
                uiScrollPanel.transform.localPosition = new(-25f, -systemUnitHeight, 0f);
                goScrollView = uiScrollPanel.gameObject;

                uiScrollView = goScrollView.AddComponent<UIScrollView>();
                uiScrollView.contentPivot = UIWidget.Pivot.Center;
                uiScrollView.movement = UIScrollView.Movement.Vertical;
                uiScrollView.scrollWheelFactor = 1.5f;

                uiBGSprite.gameObject.AddComponent<UIDragScrollView>().scrollView = uiScrollView;
                uiBGSprite.gameObject.AddComponent<BoxCollider>();
                NGUITools.UpdateWidgetCollider(uiBGSprite.gameObject);

                // ScrollBar
                uiScrollBar = NGUITools.AddChild<UIScrollBar>(goAMSPanel);
                uiScrollBar.value = 0f;
                uiScrollBar.gameObject.AddComponent<BoxCollider>();
                uiScrollBar.transform.localPosition = new(uiBGSprite.width / 2f - 10, 0f, 0f);
                uiScrollBar.transform.localRotation *= Quaternion.Euler(0f, 0f, -90f);

				var uiScrollBarFore = NGUITools.AddChild<UIWidget>(uiScrollBar.gameObject);
                uiScrollBarFore.name = "DummyFore";
                uiScrollBarFore.height = 15;
                uiScrollBarFore.width = uiBGSprite.height;

				var uiScrollBarThumb = NGUITools.AddChild<UISprite>(uiScrollBar.gameObject);
                uiScrollBarThumb.name = "Thumb";
                uiScrollBarThumb.depth = uiBGSprite.depth + 1;
                uiScrollBarThumb.atlas = uiAtlasSceneEdit;
                uiScrollBarThumb.spriteName = "cm3d2_edit_slidercursor";
                uiScrollBarThumb.type = UIBasicSprite.Type.Sliced;
                uiScrollBarThumb.SetDimensions(15, 15);
                uiScrollBarThumb.gameObject.AddComponent<BoxCollider>();

                uiScrollBar.foregroundWidget = uiScrollBarFore;
                uiScrollBar.thumb = uiScrollBarThumb.transform;

                NGUITools.UpdateWidgetCollider(uiScrollBarFore.gameObject);
                NGUITools.UpdateWidgetCollider(uiScrollBarThumb.gameObject);
                uiScrollView.verticalScrollBar = uiScrollBar;

                // ScrollView内のTable
                uiTable = NGUITools.AddChild<UITable>(goScrollView);
                uiTable.pivot = UIWidget.Pivot.Center;
                uiTable.columns = 1;
                uiTable.padding = new(25f, 10f);
                uiTable.hideInactive = true;
                uiTable.keepWithinPanel = true;
                uiTable.sorting = UITable.Sorting.Custom;
                uiTable.onCustomSort = (Comparison<Transform>)this.sortGridByXMLOrder;
                //uiTable.onReposition    = this.OnRepositionTable;
                goScrollViewTable = uiTable.gameObject;
				//uiScrollView.centerOnChild = goScrollViewTable.AddComponent<UICenterOnChild>();

				// ドラッグ用タブ（タイトル部分）
				var uiSpriteTitleTab = NGUITools.AddChild<UISprite>(goAMSPanel);
                uiSpriteTitleTab.name = "TitleTab";
                uiSpriteTitleTab.depth = uiBGSprite.depth - 1;
                uiSpriteTitleTab.atlas = uiAtlasDialog;
                uiSpriteTitleTab.spriteName = "cm3d2_dialog_frame";
                uiSpriteTitleTab.type = UIBasicSprite.Type.Sliced;
                uiSpriteTitleTab.SetDimensions(300, 80);
                uiSpriteTitleTab.autoResizeBoxCollider = true;

				//uiSpriteTitleTab.gameObject.AddComponent<UIDragObject>().target = goAMSPanel.transform;
				//uiSpriteTitleTab.gameObject.AddComponent<UIDragObject>().dragEffect = UIDragObject.DragEffect.None;

				var uiDragObject = uiSpriteTitleTab.gameObject.AddComponent<UIDragObject>();
                uiDragObject.target = goAMSPanel.transform;
                uiDragObject.dragEffect = UIDragObject.DragEffect.None;

                uiSpriteTitleTab.gameObject.AddComponent<BoxCollider>().isTrigger = true;
                NGUITools.UpdateWidgetCollider(uiSpriteTitleTab.gameObject);
                uiSpriteTitleTab.transform.localPosition = new(uiBGSprite.width / 2f + 5f, (uiBGSprite.height - uiSpriteTitleTab.width) / 2f, 0f);
                uiSpriteTitleTab.transform.localRotation *= Quaternion.Euler(0f, 0f, -90f);

				var uiLabelTitleTab = uiSpriteTitleTab.gameObject.AddComponent<UILabel>();
                uiLabelTitleTab.depth = uiSpriteTitleTab.depth + 1;
                uiLabelTitleTab.width = uiSpriteTitleTab.width;
                uiLabelTitleTab.color = Color.white;
                uiLabelTitleTab.trueTypeFont = font;
                uiLabelTitleTab.fontSize = 18;
                uiLabelTitleTab.text = "Mods Slider " + AddModsSlider.Version;

                int conWidth = (int)(uiBGSprite.width - uiTable.padding.x * 2);
                int baseTop = (int)(uiBGSprite.height / 2f - 50);

				var goSystemUnit = NGUITools.AddChild(goAMSPanel);
                goSystemUnit.name = ("System:Undo");

				// Undoボタン
				var goUndoAll = SetCloneChild(goSystemUnit, goProfileTabCopy, "UndoAll");
                goUndoAll.transform.localPosition = new(-conWidth * 0.25f - 6, baseTop - systemUnitHeight / 2f, 0f);
                goUndoAll.AddComponent<UIDragScrollView>().scrollView = uiScrollView;

				var uiSpriteUndoAll = goUndoAll.GetComponent<UISprite>();
                uiSpriteUndoAll.SetDimensions((int)(conWidth * 0.5f) - 2, systemUnitHeight);

				var uiLabelUndoAll = FindChild(goUndoAll, "Name").GetComponent<UILabel>();
				// Localize対応。v1.17以前でも動くように
				var undoAllMonoList = uiLabelUndoAll.GetComponents<MonoBehaviour>();
                foreach(var mb in undoAllMonoList) {
                    if(mb.GetType().Name == "Localize") {
                        mb.enabled = false;
                    }
                }
                uiLabelUndoAll.width = uiSpriteUndoAll.width - 10;
                uiLabelUndoAll.fontSize = 22;
                uiLabelUndoAll.spacingX = 0;
                uiLabelUndoAll.supportEncoding = true;
                uiLabelUndoAll.text = "[111111]UndoAll";

				var uiButtonUndoAll = goUndoAll.GetComponent<UIButton>();
                uiButtonUndoAll.defaultColor = new(1f, 1f, 1f, 0.8f);
                EventDelegate.Set(uiButtonUndoAll.onClick, new EventDelegate.Callback(this.OnClickUndoAll));

                FindChild(goUndoAll, "SelectCursor").GetComponent<UISprite>().SetDimensions(16, 16);
                FindChild(goUndoAll, "SelectCursor").SetActive(false);
                NGUITools.UpdateWidgetCollider(goUndoAll);
                goUndoAll.SetActive(true);

				// Resetボタン
				var goResetAll = SetCloneChild(goSystemUnit, goUndoAll, "ResetAll");
                goResetAll.transform.localPosition = new(conWidth * 0.25f - 4, baseTop - systemUnitHeight / 2f, 0f);

				var uiLabelResetAll = FindChild(goResetAll, "Name").GetComponent<UILabel>();
                uiLabelResetAll.text = "[111111]ResetAll";

				var uiButtonResetAll = goResetAll.GetComponent<UIButton>();
                uiButtonResetAll.defaultColor = new(1f, 1f, 1f, 0.8f);
                EventDelegate.Set(uiButtonResetAll.onClick, new EventDelegate.Callback(this.OnClickResetAll));

                NGUITools.UpdateWidgetCollider(goResetAll);
                goResetAll.SetActive(true);

                #endregion



                // 拡張セーブデータ読込
                Debug.Log(LogLabel + "Loading ExternalSaveData...");
                Debug.Log("----------------ExternalSaveData----------------");
                getExSaveData();
                Debug.Log("------------------------------------------------");



                #region addTableContents

                // ModsParamの設定に従ってボタン・スライダー追加
                for (var i = 0; i < mp.KeyCount; i++) {
					var key = mp.sKey[i];

                    if (!mp.bVisible[key]) continue;

                    uiValueLable[key] = new();
					var modeDesc = $"{mp.sDescription[key]} ({key})";

					// ModUnit：modタグ単位のまとめオブジェクト ScrollViewGridの子
					var goModUnit = NGUITools.AddChild(goScrollViewTable);
                    goModUnit.name = ("Unit:" + key);
                    trModUnit[key] = goModUnit.transform;

					// プロフィールタブ複製・追加
					var goHeaderButton = SetCloneChild(goModUnit, goProfileTabCopy, "Header:" + key);
                    goHeaderButton.SetActive(true);
                    goHeaderButton.AddComponent<UIDragScrollView>().scrollView = uiScrollView;
					var uiHeaderButton = goHeaderButton.GetComponent<UIButton>();
                    EventDelegate.Set(uiHeaderButton.onClick, new EventDelegate.Callback(this.OnClickHeaderButton));
                    setButtonColor(uiHeaderButton, mp.IsToggle(key) ? mp.bEnabled[key] : false);

					// 白地Sprite
					var uiSpriteHeaderButton = goHeaderButton.GetComponent<UISprite>();
                    uiSpriteHeaderButton.type = UIBasicSprite.Type.Sliced;
                    uiSpriteHeaderButton.SetDimensions(conWidth, 40);

					var uiLabelHeader = FindChild(goHeaderButton, "Name").GetComponent<UILabel>();
                    uiLabelHeader.width = uiSpriteHeaderButton.width - 20;
                    uiLabelHeader.height = 30;
                    uiLabelHeader.trueTypeFont = font;
                    uiLabelHeader.fontSize = 22;
                    uiLabelHeader.spacingX = 0;
                    uiLabelHeader.multiLine = false;
                    uiLabelHeader.overflowMethod = UILabel.Overflow.ClampContent;
                    uiLabelHeader.supportEncoding = true;
                    uiLabelHeader.text = $"[000000]{modeDesc}[-]";
                    uiLabelHeader.gameObject.AddComponent<UIDragScrollView>().scrollView = uiScrollView;

					// 金枠Sprite
					var uiSpriteHeaderCursor = FindChild(goHeaderButton, "SelectCursor").GetComponent<UISprite>();
                    uiSpriteHeaderCursor.gameObject.SetActive(mp.IsToggle(key) ? mp.bEnabled[key] : false);

                    NGUITools.UpdateWidgetCollider(goHeaderButton);

                    // スライダーならUndo/Resetボタンとスライダー追加
                    if (mp.IsSlider(key)) {
                        uiSpriteHeaderButton.SetDimensions((int)(conWidth * 0.8f), 40);
                        uiLabelHeader.width = uiSpriteHeaderButton.width - 20;
                        uiHeaderButton.transform.localPosition = new(-conWidth * 0.1f, 0f, 0f);

						// Undoボタン
						var goUndo = SetCloneChild(goModUnit, goProfileTabCopy, "Undo:" + key);
                        goUndo.transform.localPosition = new(conWidth * 0.4f + 2, 10.5f, 0f);
                        goUndo.AddComponent<UIDragScrollView>().scrollView = uiScrollView;

						var uiSpriteUndo = goUndo.GetComponent<UISprite>();
                        uiSpriteUndo.SetDimensions((int)(conWidth * 0.2f) - 2, 19);

						var uiLabelUndo = FindChild(goUndo, "Name").GetComponent<UILabel>();
						// Localize対応。v1.17以前でも動くように
						var undoMonoList = uiLabelUndo.GetComponents<MonoBehaviour>();
                        foreach (var mb in undoMonoList) {
                            if (mb.GetType().Name == "Localize") {
                                mb.enabled = false;
                            }
                        }
                        uiLabelUndo.width = uiSpriteUndo.width - 10;
                        uiLabelUndo.fontSize = 14;
                        uiLabelUndo.spacingX = 0;
                        uiLabelUndo.supportEncoding = true;
                        uiLabelUndo.text = "[111111]Undo";

						var uiButtonUndo = goUndo.GetComponent<UIButton>();
                        uiButtonUndo.defaultColor = new(1f, 1f, 1f, 0.8f);

                        EventDelegate.Set(uiButtonUndo.onClick, new EventDelegate.Callback(this.OnClickUndoButton));
                        FindChild(goUndo, "SelectCursor").GetComponent<UISprite>().SetDimensions(16, 16);
                        FindChild(goUndo, "SelectCursor").SetActive(false);
                        NGUITools.UpdateWidgetCollider(goUndo);
                        goUndo.SetActive(true);

						// Resetボタン
						var goReset = SetCloneChild(goModUnit, goProfileTabCopy, "Reset:" + key);
                        goReset.AddComponent<UIDragScrollView>().scrollView = uiScrollView;
                        goReset.transform.localPosition = new(conWidth * 0.4f + 2, -10.5f, 0f);

						var uiSpriteReset = goReset.GetComponent<UISprite>();
                        uiSpriteReset.SetDimensions((int)(conWidth * 0.2f) - 2, 19);

						var uiLabelReset = FindChild(goReset, "Name").GetComponent<UILabel>();
						// Localize対応。v1.17以前でも動くように
						var resetMonoList = uiLabelReset.GetComponents<MonoBehaviour>();
                        foreach (var mb in resetMonoList) {
                            if (mb.GetType().Name == "Localize") {
                                mb.enabled = false;
                            }
                        }
                        uiLabelReset.width = uiSpriteReset.width - 10;
                        uiLabelReset.fontSize = 14;
                        uiLabelReset.spacingX = 0;
                        uiLabelReset.supportEncoding = true;
                        uiLabelReset.text = "[111111]Reset";

						var uiButtonReset = goReset.GetComponent<UIButton>();
                        uiButtonReset.defaultColor = new(1f, 1f, 1f, 0.8f);

                        EventDelegate.Set(uiButtonReset.onClick, new EventDelegate.Callback(this.OnClickResetButton));
                        FindChild(goReset, "SelectCursor").GetComponent<UISprite>().SetDimensions(16, 16);
                        FindChild(goReset, "SelectCursor").SetActive(false);
                        NGUITools.UpdateWidgetCollider(goReset);
                        goReset.SetActive(true);


                        for (var j = 0; j < mp.ValCount(key); j++) {
							var prop = mp.sPropName[key][j];

                            if (!mp.bVVisible[key][prop]) continue;

							var value = mp.fValue[key][prop];
							var vmin = mp.fVmin[key][prop];
							var vmax = mp.fVmax[key][prop];
							var label = mp.sLabel[key][prop];
							var vType = mp.sVType[key][prop];

							// スライダーをModUnitに追加
							var goSliderUnit = SetCloneChild(goModUnit, goTestSliderUnit, "SliderUnit");
                            goSliderUnit.transform.localPosition = new Vector3(0f, j * -70f - uiSpriteHeaderButton.height - 20f, 0f);
                            goSliderUnit.AddComponent<UIDragScrollView>().scrollView = uiScrollView;

                            // フレームサイズ
                            goSliderUnit.GetComponent<UISprite>().SetDimensions(conWidth, 50);

                            // スライダー設定
                            UISlider uiModSlider = FindChild(goSliderUnit, "Slider").GetComponent<UISlider>();
                            uiModSlider.name = $"Slider:{key}:{prop}";
                            uiModSlider.value = codecSliderValue(key, prop);
                            if (vType == "int") uiModSlider.numberOfSteps = (int)(vmax - vmin + 1);
                            EventDelegate.Add(uiModSlider.onChange, new EventDelegate.Callback(this.OnChangeSlider));

                            // スライダーラベル設定
                            FindChild(goSliderUnit, "Label").GetComponent<UILabel>().text = label;
                            FindChild(goSliderUnit, "Label").AddComponent<UIDragScrollView>().scrollView = uiScrollView;

							// スライダー値ラベル参照取得
							var goValueLabel = FindChild(goSliderUnit, "Value");
                            goValueLabel.name = $"Value:{key}:{prop}";
                            uiValueLable[key][prop] = goValueLabel.GetComponent<UILabel>();
                            uiValueLable[key][prop].multiLine = false;
                            EventDelegate.Set(goValueLabel.GetComponent<UIInput>().onSubmit, this.OnSubmitSliderValueInput);

                            // スライダー有効状態設定
                            //goSliderUnit.SetActive( !mp.IsToggle(key) || mp.bEnabled[key] && mp.CheckWS(key) );
                            goSliderUnit.SetActive(false);
                        }
                    }

                    // 金枠Sprite
                    uiSpriteHeaderCursor.type = UIBasicSprite.Type.Sliced;
                    uiSpriteHeaderCursor.SetDimensions(uiSpriteHeaderButton.width - 4, uiSpriteHeaderButton.height - 4);
                }

                #endregion

                uiTable.Reposition();
                goAMSPanel.SetActive(false);

                //WriteTrans("UI Root");

            } catch (Exception ex) { Debug.Log($"{LogLabel}initialize() {ex}"); return false; }

            return true;
        }

        private void finalize() {
            bInitCompleted = false;
            visible = false;
            mp = null;

            maid = null;
            goAMSPanel = null;
            goScrollView = null;
            goScrollViewTable = null;

            uiValueLable.Clear();
        }

		//----

		public void toggleActiveOnWideSlider() => toggleActiveOnWideSlider(mp.bEnabled["WIDESLIDER"]);
		public void toggleActiveOnWideSlider(bool b) {
            try {

                foreach (Transform t in goScrollViewTable.transform) {
					var goType = getTag(t, 0);
					var goKey = getTag(t, 1);

                    if (goType == "System") continue;

                    if (mp.bOnWideSlider[goKey]) {
						var s = (b ? "[000000]" : "[FF0000]WS必須 [-]") + $"{mp.sDescription[goKey]} ({goKey})";
                        t.GetComponentsInChildren<UILabel>()[0].text = s;

						var uiButton = t.GetComponentsInChildren<UIButton>()[0];
                        uiButton.isEnabled = b;
                        if (!(b && mp.IsSlider(goKey))) setButtonColor(uiButton, b && mp.bEnabled[goKey]);

                        if (!b) {
                            foreach (Transform tc in t) {
								var gocType = getTag(tc, 0);
                                if (gocType == "SliderUnit" || gocType == "Spacer") tc.gameObject.SetActive(b);
                            }
                        }
                    }
                }
                uiTable.repositionNow = true;

            } catch (Exception ex) { Debug.Log($"{LogLabel}toggleActiveOnWideSlider() {ex}"); }
        }

        private void undoSliderValue(string key) {
            try {
                foreach (Transform tr in trModUnit[key]) {
                    if (tr.name == "SliderUnit") {
						var slider = FindChildByTag(tr, "Slider").GetComponent<UISlider>();
						var prop = getTag(slider, 2);

                        mp.fValue[key][prop] = undoValue[key][prop];
                        slider.value = codecSliderValue(key, prop);

                        uiValueLable[key][prop].text = $"{codecSliderValue(key, prop, slider.value):F2}";
                        uiValueLable[key][prop].gameObject.GetComponent<UIInput>().value = uiValueLable[key][prop].text;
                        //Debug.LogWarning(key + "#"+ getTag(slider, 2) +" = "+ undoValue[key][prop]);
                    }
                }
            } catch (Exception ex) { Debug.Log($"{LogLabel}undoSliderValue() {ex}"); }
        }

        private void resetSliderValue(string key) {
            try {
                foreach (Transform tr in trModUnit[key]) {
                    if (tr.name == "SliderUnit") {
						var slider = FindChildByTag(tr, "Slider").GetComponent<UISlider>();
						var prop = getTag(slider, 2);

                        mp.fValue[key][prop] = mp.fVdef[key][prop];
                        slider.value = codecSliderValue(key, prop);

                        uiValueLable[key][prop].text = $"{codecSliderValue(key, prop, slider.value):F2}";
                        uiValueLable[key][prop].gameObject.GetComponent<UIInput>().value = uiValueLable[key][prop].text;

                        //Debug.LogWarning(key + "#"+ getTag(slider, 2) +" = "+ mp.fVdef[key][prop]);
                    }
                }
            } catch (Exception ex) { Debug.Log($"{LogLabel}resetSliderValue() {ex}"); }
        }


        private int sortGridByXMLOrder(Transform t1, Transform t2) {
            try {
				var type1 = t1.name.Split(':')[0];
				var type2 = t2.name.Split(':')[0];
				var key1 = t1.name.Split(':')[1];
				var key2 = t2.name.Split(':')[1];
				var n = mp.sKey.IndexOf(key1);
				var m = mp.sKey.IndexOf(key2);

				//Debug.Log(t1.name +" comp "+ t2.name);

				var order = new Dictionary<string, int>()
                { {"System", -1}, {"Unit", 0}, {"Panel", 1}, {"Header", 2}, {"Slider", 3}, {"Spacer", 4} };

                if (n == m) {
                    if (type1 == "Slider" && type2 == "Slider") {
						var l = Array.IndexOf(mp.sPropName[key1], t1.name.Split(':')[2]);
						var k = Array.IndexOf(mp.sPropName[key2], t2.name.Split(':')[2]);

                        return l - k;
                    } else return order[type1] - order[type2];
                } else return n - m;
            } catch (Exception ex) { Debug.Log($"{LogLabel}sortGridByXMLOrder() {ex}"); return 0; }
        }

        private void setSliderVisible(string key, bool b) {
            foreach (Transform tc in trModUnit[key]) {
				var type = getTag(tc, 0);
                if (type == "SliderUnit" || type == "Spacer") tc.gameObject.SetActive(b);
            }

            uiTable.repositionNow = true;
        }

        private void setButtonColor(string key, bool b) {
            setButtonColor(FindChild(trModUnit[key], "Header:" + key).GetComponent<UIButton>(), b);
        }
        private void setButtonColor(UIButton button, bool b) {
			var color = button.defaultColor;

            if (mp.IsToggle(getTag(button, 1))) {
                button.defaultColor = new(color.r, color.g, color.b, b ? 1f : 0.5f);
                FindChild(button.gameObject, "SelectCursor").SetActive(b);
            } else {
                button.defaultColor = new(color.r, color.g, color.b, b ? 1f : 0.75f);
            }
        }

        private void windowTweenFinished() {
            goScrollView.SetActive(true);
        }

		private string getTag(Component co, int n) => getTag(co.gameObject, n);
		private string getTag(GameObject go, int n) {
            return (go.name.Split(':') != null) ? go.name.Split(':')[n] : "";
        }

        private float codecSliderValue(string key, string prop) {
			var value = mp.fValue[key][prop];
			var vmin = mp.fVmin[key][prop];
			var vmax = mp.fVmax[key][prop];
			var vType = mp.sVType[key][prop];

            if (value < vmin) value = vmin;
            if (value > vmax) value = vmax;

            if (vType == "scale" && vmin < 1f) {
                if (vmin < 0f) vmin = 0f;
                if (value < 0f) value = 0f;

                return (value < 1f) ? (value - vmin) / (1f - vmin) * 0.5f : 0.5f + (value - 1f) / (vmax - 1f) * 0.5f;
            } else if (vType == "int") {
				var dvalue = (decimal)value;
				var dvmin = (decimal)vmin;
				var dvmax = (decimal)vmax;

                return (float)Math.Round((dvalue - dvmin) / (dvmax - dvmin), 1, MidpointRounding.AwayFromZero);
            } else {
                return (value - vmin) / (vmax - vmin);
            }
        }

        private float codecSliderValue(string key, string prop, float value) {
			var vmin = mp.fVmin[key][prop];
			var vmax = mp.fVmax[key][prop];
			var vType = mp.sVType[key][prop];

            if (value < 0f) value = 0f;
            if (value > 1f) value = 1f;

            if (vType == "scale" && vmin < 1f) {
                if (vmin < 0f) vmin = 0f;
                if (value < 0f) value = 0f;

                return (value < 0.5f) ? vmin + (1f - vmin) * value * 2f : 1 + (vmax - 1f) * (value - 0.5f) * 2;
            } else if (vType == "int") {
				var dvalue = (decimal)value;
				var dvmin = (decimal)vmin;
				var dvmax = (decimal)vmax;

                return (float)Math.Round(vmin + (vmax - vmin) * value, 0, MidpointRounding.AwayFromZero);
            } else {
                return vmin + (vmax - vmin) * value;
            }
        }


        //--------

        private void notifyMaidVoicePitchOnChange() {
            this.gameObject.SendMessage("MaidVoicePitch_UpdateSliders");
        }

        public void syncExSaveDatatoSlider() {
            Debug.Log(LogLabel + "Loading ExternalPresetData...");
            Debug.Log("----------------ExternalPresetData----------------");
            getExSaveData();
            Debug.Log("------------------------------------------------");
            try {
                for (var i = 0; i < mp.KeyCount; i++) {
					var key = mp.sKey[i];

                    foreach (Transform tr in trModUnit[key]) {

                        if (mp.IsToggle(key)) {
                            setButtonColor(key, mp.bEnabled[key]);

                        }

                        if (mp.IsSlider(key)) {
                            if (tr.name == "SliderUnit") {
								var slider = FindChildByTag(tr, "Slider").GetComponent<UISlider>();
                                var prop = getTag(slider, 2);

                                slider.value = codecSliderValue(key, prop);
                                uiValueLable[key][prop].text = $"{codecSliderValue(key, prop, slider.value):F2}";
                                uiValueLable[key][prop].gameObject.GetComponent<UIInput>().value = uiValueLable[key][prop].text;
                                //Debug.LogWarning(key + "#"+ getTag(slider, 2) +" = "+ mp.fVdef[key][prop]);
                            }
                        }
                    }
                }
            } catch (Exception ex) { Debug.Log($"{LogLabel}syncExSaveDatatoSlider() {ex}"); }

        }


        private void getExSaveData() {
			var plugin = "CM3D2.MaidVoicePitch";
            for (var i = 0; i < mp.KeyCount; i++) {
				var key = mp.sKey[i];
                undoValue[key] = new();

                if (mp.IsToggle(key)) {
                    mp.bEnabled[key] = ExSaveData.GetBool(maid, plugin, key, false);
                    undoValue[key]["enable"] = (mp.bEnabled[key]) ? 1f : 0f;
                    Debug.Log($"{key,-32} = {mp.bEnabled[key],-16}");
                }

                if (mp.IsSlider(key)) {
                    for (var j = 0; j < mp.ValCount(key); j++) {
						var prop = mp.sPropName[key][j];
						var f = ExSaveData.GetFloat(maid, plugin, prop, float.NaN);
                        mp.fValue[key][prop] = float.IsNaN(f) ? mp.fVdef[key][prop] : f;
                        undoValue[key][prop] = mp.fValue[key][prop];

                        Debug.Log($"{prop,-32} = {mp.fValue[key][prop]:f}");
                    }
                    if (!mp.IsToggle(key)) mp.bEnabled[key] = true;
                }
            }
        }

        private void setExSaveData() {
            for (var i = 0; i < mp.KeyCount; i++) setExSaveData(mp.sKey[i]);
        }

        private void setExSaveData(string key) {
			var plugin = "CM3D2.MaidVoicePitch";

            if (mp.IsToggle(key)) {
                ExSaveData.SetBool(maid, plugin, key, mp.bEnabled[key]);
            }

            if (mp.IsSlider(key)) {
                for (var j = 0; j < mp.ValCount(key); j++) setExSaveData(key, mp.sPropName[key][j]);
            }
        }

        private void setExSaveData(string key, string prop) {
			var plugin = "CM3D2.MaidVoicePitch";

			var value = (float)Math.Round(mp.fValue[key][prop], 3, MidpointRounding.AwayFromZero);

            ExSaveData.SetFloat(maid, plugin, prop, value);
        }

		#endregion



		#region Utility methods


		internal static Transform FindParent(Transform tr, string s) => FindParent(tr.gameObject, s).transform;
		internal static GameObject FindParent(GameObject go, string name) {
            if (go == null) return null;

			var _parent = go.transform.parent;
            while (_parent) {
                if (_parent.name == name) return _parent.gameObject;
                _parent = _parent.parent;
            }

            return null;
        }

		internal static Transform FindChild(Transform tr, string s) => FindChild(tr.gameObject, s).transform;
		internal static GameObject FindChild(GameObject go, string s) {
            if (go == null) return null;
            GameObject target = null;

            foreach (Transform tc in go.transform) {
                if (tc.gameObject.name == s) return tc.gameObject;
                target = FindChild(tc.gameObject, s);
                if (target) return target;
            }

            return null;
        }

		internal static Transform FindChildByTag(Transform tr, string s) => FindChildByTag(tr.gameObject, s).transform;
		internal static GameObject FindChildByTag(GameObject go, string s) {
            if (go == null) return null;
            GameObject target = null;

            foreach (Transform tc in go.transform) {
                if (tc.gameObject.name.Contains(s)) return tc.gameObject;
                target = FindChild(tc.gameObject, s);
                if (target) return target;
            }

            return null;
        }


        internal static void SetChild(GameObject parent, GameObject child) {
            child.layer = parent.layer;
            child.transform.parent = parent.transform;
            child.transform.localPosition = Vector3.zero;
            child.transform.localScale = Vector3.one;
            child.transform.rotation = Quaternion.identity;
        }

        internal static GameObject SetCloneChild(GameObject parent, GameObject orignal, string name) {
			var clone = UnityEngine.Object.Instantiate(orignal);
            if (!clone) return null;

            clone.name = name;
            SetChild(parent, clone);

            return clone;
        }

        internal static void ReleaseChild(GameObject child) {
            child.transform.parent = null;
            child.SetActive(false);
        }

        internal static void DestoryChild(GameObject parent, string name) {
			var child = FindChild(parent, name);
            if (child) {
                child.transform.parent = null;
                GameObject.Destroy(child);
            }
        }

        internal static UIAtlas FindAtlas(string s) {
            return ((new List<UIAtlas>(Resources.FindObjectsOfTypeAll<UIAtlas>())).FirstOrDefault(a => a.name == s));
        }

        internal static void WriteTrans(string s) {
			var go = GameObject.Find(s);
            if (!go) return;

            WriteTrans(go.transform, 0, null);
        }
		internal static void WriteTrans(Transform t) => WriteTrans(t, 0, null);
		internal static void WriteTrans(Transform t, int level, StreamWriter writer) {
            if (level == 0) writer = new($".\\{t.name}.txt", false);
            if (writer == null) return;

			var s = "";
            for (var i = 0; i < level; i++) s += "    ";
            writer.WriteLine(s + level + "," + t.name);
            foreach (Transform tc in t) {
                WriteTrans(tc, level + 1, writer);
            }

            if (level == 0) writer.Close();
        }

        internal static void WriteChildrenComponent(GameObject go) {
            WriteComponent(go);

            foreach (Transform tc in go.transform) {
                WriteChildrenComponent(tc.gameObject);
            }
        }

        internal static void WriteComponent(GameObject go) {
			var compos = go.GetComponents<Component>();
            foreach (var c in compos) { Debug.Log($"{go.name}:{c.GetType().Name}"); }
        }

        #endregion

        #region Public methods
        static public void AddExternalModsParam(ExternalModsParam emp) {
            externalModsParamList.Add(emp);
        }
        #endregion
    }
}

