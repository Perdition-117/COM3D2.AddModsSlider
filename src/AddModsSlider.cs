using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using CM3D2.ExternalPreset.Managed;
using CM3D2.ExternalSaveData.Managed;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CM3D2.AddModsSlider.Plugin;

//[PluginFilter("CM3D2x64"), PluginFilter("CM3D2x86"), PluginFilter("CM3D2VRx64")]
[BepInPlugin("CM3D2.AddModsSlider", "CM3D2 AddModsSlider", "0.1.3.6")]
public class AddModsSlider : BaseUnityPlugin {

	#region Constants

	public const string PluginName = "AddModsSlider";
	public const string Version = "0.1.3.6";

	private const string MaidVoicePitchPluginId = "CM3D2.MaidVoicePitch";

	private const string LogLabel = PluginName + " : ";

	private const float TimePerInit = 0.10f;

	private const int UIRootWidth = 1920; // GemaObject.Find("UI Root").GetComponent<UIRoot>().manualWidth;
	private const int UIRootHeight = 1080; // GemaObject.Find("UI Root").GetComponent<UIRoot>().manualHeight;
	private const int ScrollViewWidth = 550;
	private const int ScrollViewHeight = 860;

	#endregion


	#region Variables

	private bool _xmlLoad = false;
	private bool _isVisible = false;
	private bool _isInitialized = false;

	private ModsParam _modsParam;
	private readonly Dictionary<string, Dictionary<string, float>> _previousValues = new();

	private Maid _currentMaid;

	private UICamera _uiCamera;
	private UIPanel _uiPanel;
	private UIPanel _uiScrollPanel;
	private UITable _uiTable;

	private Font _font;

	private readonly Dictionary<string, Transform> trModUnit = new();
	private readonly Dictionary<string, Dictionary<string, UILabel>> _uiValueLabels = new();

	internal static List<ExternalModsParam> ExternalModParameters { get; } = new();

	#endregion


	#region Nested classes

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
	private void Start() {
		SceneManager.sceneLoaded += OnSceneLoaded;

		ExPreset.loadNotify.AddListener(syncExSaveDatatoSlider);
	}

	private void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
		if (scene.name == "SceneTitle") {
			_font = GameObject.Find("SystemUI Root").GetComponentsInChildren<UILabel>()[0].trueTypeFont;
		} else if (scene.name == "SceneEdit") {
			_modsParam = new();
			if (_xmlLoad = _modsParam.Init()) {
				StartCoroutine(InitializeCoroutine());
			}
		} else {
			Finalize();
		}
	}

	public void Update() {
		if (SceneManager.GetActiveScene().name == "SceneEdit" && _isInitialized) {
			if (Input.GetKeyDown(KeyCode.F5)) {
				_uiPanel.gameObject.SetActive(_isVisible = !_isVisible);
				//WriteTrans("UI Root");
			}
		}
	}

	#endregion


	#region Callbacks

	public void OnClickHeaderButton() {
		try {
			var key = GetTag(UIButton.current, 1);
			var enabled = false;

			if (_modsParam.IsToggle(key)) {
				enabled = !_modsParam.bEnabled[key];
				_modsParam.bEnabled[key] = enabled;
				SetExternalSaveData(key);

				NotifyMaidVoicePitchOnChange();

				// WIDESLIDER有効化/無効化に合わせて、依存項目UIを表示/非表示
				if (key == "WIDESLIDER") {
					toggleActiveOnWideSlider();
				}
			}

			if (_modsParam.IsSlider(key)) {
				if (!_modsParam.IsToggle(key)) {
					enabled = UIButton.current.defaultColor.a != 1f;
				}

				SetSliderVisible(key, enabled);
			}

			SetButtonColor(UIButton.current, enabled);
		} catch (Exception ex) {
			Debug.Log($"{LogLabel}OnClickToggleHeader() {ex}");
			return;
		}
	}

	public void OnClickUndoAll() {
		try {
			foreach (var key in _modsParam.sKey) {
				if (_modsParam.IsToggle(key)) {
					_modsParam.bEnabled[key] = _previousValues[key]["enable"] == 1f;
					SetExternalSaveData(key);
					NotifyMaidVoicePitchOnChange();
					SetButtonColor(key, _modsParam.bEnabled[key]);
				}

				if (_modsParam.IsSlider(key)) {
					UndoSliderValue(key);
					SetExternalSaveData(key);

					if (_modsParam.IsToggle(key)) {
						SetSliderVisible(key, _modsParam.bEnabled[key]);
					}
				}
			}
		} catch (Exception ex) {
			Debug.Log($"{LogLabel}OnClickUndoAll() {ex}");
			return;
		}
	}

	public void OnClickUndoButton() {
		var key = GetTag(UIButton.current, 1);
		UndoSliderValue(key);
		SetExternalSaveData(key);
	}

	public void OnClickResetAll() {
		try {
			foreach (var key in _modsParam.sKey) {
				if (_modsParam.IsToggle(key)) {
					_modsParam.bEnabled[key] = false;
					SetExternalSaveData(key);
					NotifyMaidVoicePitchOnChange();
					SetButtonColor(key, _modsParam.bEnabled[key]);
				}

				if (_modsParam.IsSlider(key)) {
					ResetSliderValue(key);
					SetExternalSaveData(key);

					if (_modsParam.IsToggle(key)) {
						SetSliderVisible(key, _modsParam.bEnabled[key]);
					}
				}
			}
		} catch (Exception ex) {
			Debug.Log($"{LogLabel}OnClickResetAll() {ex}");
			return;
		}
	}

	public void OnClickResetButton() {
		var key = GetTag(UIButton.current, 1);
		ResetSliderValue(key);
		SetExternalSaveData(key);
	}

	public void OnChangeSlider() {
		try {
			var key = GetTag(UIProgressBar.current, 1);
			var prop = GetTag(UIProgressBar.current, 2);
			var value = CodecSliderValue(key, prop, UIProgressBar.current.value);
			var vType = _modsParam.sVType[key][prop];

			_uiValueLabels[key][prop].text = $"{value:F2}";
			_uiValueLabels[key][prop].gameObject.GetComponent<UIInput>().value = _uiValueLabels[key][prop].text;
			_modsParam.fValue[key][prop] = value;

			SetExternalSaveData(key, prop);

			NotifyMaidVoicePitchOnChange();

			//Debug.Log(key +":"+ prop +":"+ value);
		} catch (Exception ex) {
			Debug.Log($"{LogLabel}OnChangeSlider() {ex}");
			return;
		}
	}

	public void OnSubmitSliderValueInput() {
		try {
			var key = GetTag(UIInput.current, 1);
			var prop = GetTag(UIInput.current, 2);
			UISlider slider = null;

			foreach (Transform t in UIInput.current.transform.parent.parent) {
				if (GetTag(t, 0) == "Slider") {
					slider = t.GetComponent<UISlider>();
				}
			}

			if (float.TryParse(UIInput.current.value, out var value)) {
				_modsParam.fValue[key][prop] = value;
				slider.value = CodecSliderValue(key, prop);
				UIInput.current.value = CodecSliderValue(key, prop, slider.value).ToString("F2");
				_uiValueLabels[key][prop].text = UIInput.current.value;
			}
		} catch (Exception ex) {
			Debug.Log($"{LogLabel}OnSubmitSliderValueInput() {ex}");
			return;
		}
	}

	#endregion


	#region Private methods

	private IEnumerator InitializeCoroutine() {
		while (!(_isInitialized = Initialize())) {
			yield return new WaitForSeconds(TimePerInit);
		}

		Debug.Log(LogLabel + "Initialization complete.");
	}

	private bool Initialize() {
		try {
			_currentMaid = GameMain.Instance.CharacterMgr.GetMaid(0);
			if (_currentMaid == null) {
				return false;
			}

			var uiAtlasSceneEdit = FindAtlas("AtlasSceneEdit");
			var uiAtlasDialog = FindAtlas("SystemDialog");

			var uiRoot = GameObject.Find("UI Root");
			var cameraObject = GameObject.Find("/UI Root/Camera");
			var cameraComponent = cameraObject.GetComponent<Camera>();
			_uiCamera = cameraObject.GetComponent<UICamera>();

			#region createSlider

			// スライダー作成
			var testSliderUnit = new GameObject("TestSliderUnit");
			SetChild(uiRoot, testSliderUnit);
			{
				var uiTestSliderUnitFrame = testSliderUnit.AddComponent<UISprite>();
				uiTestSliderUnitFrame.atlas = uiAtlasSceneEdit;
				uiTestSliderUnitFrame.spriteName = "cm3d2_edit_slidertitleframe";
				uiTestSliderUnitFrame.type = UIBasicSprite.Type.Sliced;
				uiTestSliderUnitFrame.SetDimensions(500, 50);

				// スライダー作成
				var uiTestSlider = NGUITools.AddChild<UISlider>(testSliderUnit);
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
				var uiTestSliderLabel = NGUITools.AddChild<UILabel>(testSliderUnit);
				uiTestSliderLabel.name = "Label";
				uiTestSliderLabel.trueTypeFont = _font;
				uiTestSliderLabel.fontSize = 20;
				uiTestSliderLabel.text = "テストスライダー";
				uiTestSliderLabel.width = 110;
				uiTestSliderLabel.overflowMethod = UILabel.Overflow.ShrinkContent;

				uiTestSliderLabel.transform.localPosition = new(-190f, 0f, 0f);

				// 値ラベル・インプット作成
				var uiTestSliderValueBase = NGUITools.AddChild<UISprite>(testSliderUnit);
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
				uiTestSliderValueLabel.trueTypeFont = _font;
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
			testSliderUnit.SetActive(false);

			#endregion


			// ボタンはgoProfileTabをコピー
			var profileTabCopy = Instantiate(FindChild(uiRoot.transform.Find("ProfilePanel").Find("Comment").gameObject, "ProfileTab"));
			EventDelegate.Remove(profileTabCopy.GetComponent<UIButton>().onClick, new EventDelegate.Callback(ProfileMgr.Instance.ChangeCommentTab));
			profileTabCopy.SetActive(false);


			#region createPanel

			// ModsSliderPanel作成
			var uiPanelPosition = new Vector3(UIRootWidth / 2f - 15f - ScrollViewWidth / 2f - 50f, 40f, 0f);
			var systemUnitHeight = 30;

			// 親Panel
			_uiPanel = NGUITools.AddChild<UIPanel>(uiRoot);
			_uiPanel.name = "ModsSliderPanel";
			_uiPanel.transform.localPosition = uiPanelPosition;
			var goUiPanel = _uiPanel.gameObject;

			// 背景
			var uiBGSprite = NGUITools.AddChild<UISprite>(goUiPanel);
			uiBGSprite.name = "BG";
			uiBGSprite.atlas = uiAtlasSceneEdit;
			uiBGSprite.spriteName = "cm3d2_edit_window_l";
			uiBGSprite.type = UIBasicSprite.Type.Sliced;
			uiBGSprite.SetDimensions(ScrollViewWidth, ScrollViewHeight);

			// ScrollViewPanel
			_uiScrollPanel = NGUITools.AddChild<UIPanel>(goUiPanel);
			_uiScrollPanel.name = "ScrollView";
			_uiScrollPanel.sortingOrder = _uiPanel.sortingOrder + 1;
			_uiScrollPanel.clipping = UIDrawCall.Clipping.SoftClip;
			_uiScrollPanel.SetRect(0f, 0f, uiBGSprite.width, uiBGSprite.height - 110 - systemUnitHeight);
			_uiScrollPanel.transform.localPosition = new(-25f, -systemUnitHeight, 0f);

			var uiScrollView = _uiScrollPanel.gameObject.AddComponent<UIScrollView>();
			uiScrollView.contentPivot = UIWidget.Pivot.Center;
			uiScrollView.movement = UIScrollView.Movement.Vertical;
			uiScrollView.scrollWheelFactor = 1.5f;

			uiBGSprite.gameObject.AddComponent<UIDragScrollView>().scrollView = uiScrollView;
			uiBGSprite.gameObject.AddComponent<BoxCollider>();
			NGUITools.UpdateWidgetCollider(uiBGSprite.gameObject);

			// ScrollBar
			var uiScrollBar = NGUITools.AddChild<UIScrollBar>(goUiPanel);
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
			_uiTable = NGUITools.AddChild<UITable>(_uiScrollPanel.gameObject);
			_uiTable.pivot = UIWidget.Pivot.Center;
			_uiTable.columns = 1;
			_uiTable.padding = new(25f, 10f);
			_uiTable.hideInactive = true;
			_uiTable.keepWithinPanel = true;
			_uiTable.sorting = UITable.Sorting.Custom;
			_uiTable.onCustomSort = SortGrid;
			//_uiTable.onReposition    = this.OnRepositionTable;
			//uiScrollView.centerOnChild = _uiTable.gameObject.AddComponent<UICenterOnChild>();

			// ドラッグ用タブ（タイトル部分）
			var uiSpriteTitleTab = NGUITools.AddChild<UISprite>(goUiPanel);
			uiSpriteTitleTab.name = "TitleTab";
			uiSpriteTitleTab.depth = uiBGSprite.depth - 1;
			uiSpriteTitleTab.atlas = uiAtlasDialog;
			uiSpriteTitleTab.spriteName = "cm3d2_dialog_frame";
			uiSpriteTitleTab.type = UIBasicSprite.Type.Sliced;
			uiSpriteTitleTab.SetDimensions(300, 80);
			uiSpriteTitleTab.autoResizeBoxCollider = true;

			//uiSpriteTitleTab.gameObject.AddComponent<UIDragObject>().target = uiPanelGameObject.transform;
			//uiSpriteTitleTab.gameObject.AddComponent<UIDragObject>().dragEffect = UIDragObject.DragEffect.None;

			var uiDragObject = uiSpriteTitleTab.gameObject.AddComponent<UIDragObject>();
			uiDragObject.target = goUiPanel.transform;
			uiDragObject.dragEffect = UIDragObject.DragEffect.None;

			uiSpriteTitleTab.gameObject.AddComponent<BoxCollider>().isTrigger = true;
			NGUITools.UpdateWidgetCollider(uiSpriteTitleTab.gameObject);
			uiSpriteTitleTab.transform.localPosition = new(uiBGSprite.width / 2f + 5f, (uiBGSprite.height - uiSpriteTitleTab.width) / 2f, 0f);
			uiSpriteTitleTab.transform.localRotation *= Quaternion.Euler(0f, 0f, -90f);

			var uiLabelTitleTab = uiSpriteTitleTab.gameObject.AddComponent<UILabel>();
			uiLabelTitleTab.depth = uiSpriteTitleTab.depth + 1;
			uiLabelTitleTab.width = uiSpriteTitleTab.width;
			uiLabelTitleTab.color = Color.white;
			uiLabelTitleTab.trueTypeFont = _font;
			uiLabelTitleTab.fontSize = 18;
			uiLabelTitleTab.text = "Mods Slider " + Version;

			var controlWidth = (int)(uiBGSprite.width - _uiTable.padding.x * 2);
			var baseTop = (int)(uiBGSprite.height / 2f - 50);

			var goSystemUnit = NGUITools.AddChild(goUiPanel);
			goSystemUnit.name = "System:Undo";

			// Undoボタン
			var goUndoAll = SetCloneChild(goSystemUnit, profileTabCopy, "UndoAll");
			goUndoAll.transform.localPosition = new(-controlWidth * 0.25f - 6, baseTop - systemUnitHeight / 2f, 0f);
			goUndoAll.AddComponent<UIDragScrollView>().scrollView = uiScrollView;

			var uiSpriteUndoAll = goUndoAll.GetComponent<UISprite>();
			uiSpriteUndoAll.SetDimensions((int)(controlWidth * 0.5f) - 2, systemUnitHeight);

			var uiLabelUndoAll = FindChild(goUndoAll, "Name").GetComponent<UILabel>();
			// Localize対応。v1.17以前でも動くように
			foreach (var monoBehaviour in uiLabelUndoAll.GetComponents<MonoBehaviour>()) {
				if (monoBehaviour.GetType().Name == "Localize") {
					monoBehaviour.enabled = false;
				}
			}
			uiLabelUndoAll.width = uiSpriteUndoAll.width - 10;
			uiLabelUndoAll.fontSize = 22;
			uiLabelUndoAll.spacingX = 0;
			uiLabelUndoAll.supportEncoding = true;
			uiLabelUndoAll.text = "[111111]UndoAll";

			var uiButtonUndoAll = goUndoAll.GetComponent<UIButton>();
			uiButtonUndoAll.defaultColor = new(1f, 1f, 1f, 0.8f);
			EventDelegate.Set(uiButtonUndoAll.onClick, new EventDelegate.Callback(OnClickUndoAll));

			FindChild(goUndoAll, "SelectCursor").GetComponent<UISprite>().SetDimensions(16, 16);
			FindChild(goUndoAll, "SelectCursor").SetActive(false);
			NGUITools.UpdateWidgetCollider(goUndoAll);
			goUndoAll.SetActive(true);

			// Resetボタン
			var goResetAll = SetCloneChild(goSystemUnit, goUndoAll, "ResetAll");
			goResetAll.transform.localPosition = new(controlWidth * 0.25f - 4, baseTop - systemUnitHeight / 2f, 0f);

			var uiLabelResetAll = FindChild(goResetAll, "Name").GetComponent<UILabel>();
			uiLabelResetAll.text = "[111111]ResetAll";

			var uiButtonResetAll = goResetAll.GetComponent<UIButton>();
			uiButtonResetAll.defaultColor = new(1f, 1f, 1f, 0.8f);
			EventDelegate.Set(uiButtonResetAll.onClick, new EventDelegate.Callback(OnClickResetAll));

			NGUITools.UpdateWidgetCollider(goResetAll);
			goResetAll.SetActive(true);

			#endregion


			// 拡張セーブデータ読込
			Debug.Log(LogLabel + "Loading ExternalSaveData...");
			Debug.Log("----------------ExternalSaveData----------------");
			GetExternalSaveData();
			Debug.Log("------------------------------------------------");


			#region addTableContents

			// ModsParamの設定に従ってボタン・スライダー追加
			foreach (var key in _modsParam.sKey) {
				if (!_modsParam.bVisible[key]) {
					continue;
				}

				_uiValueLabels[key] = new();
				var modDescription = $"{_modsParam.sDescription[key]} ({key})";

				// ModUnit：modタグ単位のまとめオブジェクト ScrollViewGridの子
				var goModUnit = NGUITools.AddChild(_uiTable.gameObject);
				goModUnit.name = "Unit:" + key;
				trModUnit[key] = goModUnit.transform;

				// プロフィールタブ複製・追加
				var goHeaderButton = SetCloneChild(goModUnit, profileTabCopy, "Header:" + key);
				goHeaderButton.SetActive(true);
				goHeaderButton.AddComponent<UIDragScrollView>().scrollView = uiScrollView;
				var uiHeaderButton = goHeaderButton.GetComponent<UIButton>();
				EventDelegate.Set(uiHeaderButton.onClick, new EventDelegate.Callback(OnClickHeaderButton));
				SetButtonColor(uiHeaderButton, _modsParam.IsToggle(key) && _modsParam.bEnabled[key]);

				// 白地Sprite
				var uiSpriteHeaderButton = goHeaderButton.GetComponent<UISprite>();
				uiSpriteHeaderButton.type = UIBasicSprite.Type.Sliced;
				uiSpriteHeaderButton.SetDimensions(controlWidth, 40);

				var uiLabelHeader = FindChild(goHeaderButton, "Name").GetComponent<UILabel>();
				uiLabelHeader.width = uiSpriteHeaderButton.width - 20;
				uiLabelHeader.height = 30;
				uiLabelHeader.trueTypeFont = _font;
				uiLabelHeader.fontSize = 22;
				uiLabelHeader.spacingX = 0;
				uiLabelHeader.multiLine = false;
				uiLabelHeader.overflowMethod = UILabel.Overflow.ClampContent;
				uiLabelHeader.supportEncoding = true;
				uiLabelHeader.text = $"[000000]{modDescription}[-]";
				uiLabelHeader.gameObject.AddComponent<UIDragScrollView>().scrollView = uiScrollView;

				// 金枠Sprite
				var uiSpriteHeaderCursor = FindChild(goHeaderButton, "SelectCursor").GetComponent<UISprite>();
				uiSpriteHeaderCursor.gameObject.SetActive(_modsParam.IsToggle(key) && _modsParam.bEnabled[key]);

				NGUITools.UpdateWidgetCollider(goHeaderButton);

				// スライダーならUndo/Resetボタンとスライダー追加
				if (_modsParam.IsSlider(key)) {
					uiSpriteHeaderButton.SetDimensions((int)(controlWidth * 0.8f), 40);
					uiLabelHeader.width = uiSpriteHeaderButton.width - 20;
					uiHeaderButton.transform.localPosition = new(-controlWidth * 0.1f, 0f, 0f);

					// Undoボタン
					var goUndo = SetCloneChild(goModUnit, profileTabCopy, "Undo:" + key);
					goUndo.transform.localPosition = new(controlWidth * 0.4f + 2, 10.5f, 0f);
					goUndo.AddComponent<UIDragScrollView>().scrollView = uiScrollView;

					var uiSpriteUndo = goUndo.GetComponent<UISprite>();
					uiSpriteUndo.SetDimensions((int)(controlWidth * 0.2f) - 2, 19);

					var uiLabelUndo = FindChild(goUndo, "Name").GetComponent<UILabel>();
					// Localize対応。v1.17以前でも動くように
					foreach (var monoBehaviour in uiLabelUndo.GetComponents<MonoBehaviour>()) {
						if (monoBehaviour.GetType().Name == "Localize") {
							monoBehaviour.enabled = false;
						}
					}
					uiLabelUndo.width = uiSpriteUndo.width - 10;
					uiLabelUndo.fontSize = 14;
					uiLabelUndo.spacingX = 0;
					uiLabelUndo.supportEncoding = true;
					uiLabelUndo.text = "[111111]Undo";

					var uiButtonUndo = goUndo.GetComponent<UIButton>();
					uiButtonUndo.defaultColor = new(1f, 1f, 1f, 0.8f);

					EventDelegate.Set(uiButtonUndo.onClick, new EventDelegate.Callback(OnClickUndoButton));
					FindChild(goUndo, "SelectCursor").GetComponent<UISprite>().SetDimensions(16, 16);
					FindChild(goUndo, "SelectCursor").SetActive(false);
					NGUITools.UpdateWidgetCollider(goUndo);
					goUndo.SetActive(true);

					// Resetボタン
					var goReset = SetCloneChild(goModUnit, profileTabCopy, "Reset:" + key);
					goReset.AddComponent<UIDragScrollView>().scrollView = uiScrollView;
					goReset.transform.localPosition = new(controlWidth * 0.4f + 2, -10.5f, 0f);

					var uiSpriteReset = goReset.GetComponent<UISprite>();
					uiSpriteReset.SetDimensions((int)(controlWidth * 0.2f) - 2, 19);

					var uiLabelReset = FindChild(goReset, "Name").GetComponent<UILabel>();
					// Localize対応。v1.17以前でも動くように
					foreach (var monoBehaviour in uiLabelReset.GetComponents<MonoBehaviour>()) {
						if (monoBehaviour.GetType().Name == "Localize") {
							monoBehaviour.enabled = false;
						}
					}
					uiLabelReset.width = uiSpriteReset.width - 10;
					uiLabelReset.fontSize = 14;
					uiLabelReset.spacingX = 0;
					uiLabelReset.supportEncoding = true;
					uiLabelReset.text = "[111111]Reset";

					var uiButtonReset = goReset.GetComponent<UIButton>();
					uiButtonReset.defaultColor = new(1f, 1f, 1f, 0.8f);

					EventDelegate.Set(uiButtonReset.onClick, new EventDelegate.Callback(OnClickResetButton));
					FindChild(goReset, "SelectCursor").GetComponent<UISprite>().SetDimensions(16, 16);
					FindChild(goReset, "SelectCursor").SetActive(false);
					NGUITools.UpdateWidgetCollider(goReset);
					goReset.SetActive(true);


					for (var j = 0; j < _modsParam.sPropName[key].Length; j++) {
						var prop = _modsParam.sPropName[key][j];

						if (!_modsParam.bVVisible[key][prop]) {
							continue;
						}

						var value = _modsParam.fValue[key][prop];
						var minValue = _modsParam.fVmin[key][prop];
						var maxValue = _modsParam.fVmax[key][prop];
						var label = _modsParam.sLabel[key][prop];
						var valueType = _modsParam.sVType[key][prop];

						// スライダーをModUnitに追加
						var goSliderUnit = SetCloneChild(goModUnit, testSliderUnit, "SliderUnit");
						goSliderUnit.transform.localPosition = new Vector3(0f, j * -70f - uiSpriteHeaderButton.height - 20f, 0f);
						goSliderUnit.AddComponent<UIDragScrollView>().scrollView = uiScrollView;

						// フレームサイズ
						goSliderUnit.GetComponent<UISprite>().SetDimensions(controlWidth, 50);

						// スライダー設定
						var uiModSlider = FindChild(goSliderUnit, "Slider").GetComponent<UISlider>();
						uiModSlider.name = $"Slider:{key}:{prop}";
						uiModSlider.value = CodecSliderValue(key, prop);
						if (valueType == "int") {
							uiModSlider.numberOfSteps = (int)(maxValue - minValue + 1);
						}
						EventDelegate.Add(uiModSlider.onChange, new EventDelegate.Callback(OnChangeSlider));

						// スライダーラベル設定
						FindChild(goSliderUnit, "Label").GetComponent<UILabel>().text = label;
						FindChild(goSliderUnit, "Label").AddComponent<UIDragScrollView>().scrollView = uiScrollView;

						// スライダー値ラベル参照取得
						var goValueLabel = FindChild(goSliderUnit, "Value");
						goValueLabel.name = $"Value:{key}:{prop}";
						_uiValueLabels[key][prop] = goValueLabel.GetComponent<UILabel>();
						_uiValueLabels[key][prop].multiLine = false;
						EventDelegate.Set(goValueLabel.GetComponent<UIInput>().onSubmit, OnSubmitSliderValueInput);

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

			_uiTable.Reposition();
			goUiPanel.SetActive(false);

			//WriteTrans("UI Root");
		} catch (Exception ex) {
			Debug.Log($"{LogLabel}initialize() {ex}");
			return false;
		}

		return true;
	}

	private void Finalize() {
		_isInitialized = false;
		_isVisible = false;
		_modsParam = null;

		_currentMaid = null;

		_uiValueLabels.Clear();
	}

	//----

	public void toggleActiveOnWideSlider() => toggleActiveOnWideSlider(_modsParam.bEnabled["WIDESLIDER"]);

	public void toggleActiveOnWideSlider(bool enable) {
		try {
			foreach (Transform transform in _uiTable.gameObject.transform) {
				var goType = GetTag(transform, 0);
				var goKey = GetTag(transform, 1);

				if (goType == "System") {
					continue;
				}

				if (_modsParam.bOnWideSlider[goKey]) {
					var s = (enable ? "[000000]" : "[FF0000]WS必須 [-]") + $"{_modsParam.sDescription[goKey]} ({goKey})";
					transform.GetComponentsInChildren<UILabel>()[0].text = s;

					var uiButton = transform.GetComponentsInChildren<UIButton>()[0];
					uiButton.isEnabled = enable;
					if (!(enable && _modsParam.IsSlider(goKey))) {
						SetButtonColor(uiButton, enable && _modsParam.bEnabled[goKey]);
					}

					if (!enable) {
						foreach (Transform transformChild in transform) {
							var gocType = GetTag(transformChild, 0);
							if (gocType == "SliderUnit" || gocType == "Spacer") {
								transformChild.gameObject.SetActive(enable);
							}
						}
					}
				}
			}

			_uiTable.repositionNow = true;
		} catch (Exception ex) {
			Debug.Log($"{LogLabel}toggleActiveOnWideSlider() {ex}");
		}
	}

	private void UndoSliderValue(string key) {
		try {
			foreach (Transform transform in trModUnit[key]) {
				if (transform.name == "SliderUnit") {
					var slider = FindChildByTag(transform, "Slider").GetComponent<UISlider>();
					var prop = GetTag(slider, 2);

					_modsParam.fValue[key][prop] = _previousValues[key][prop];

					SetSliderValue(slider, key);
				}
			}
		} catch (Exception ex) {
			Debug.Log($"{LogLabel}UndoSliderValue() {ex}");
		}
	}

	private void ResetSliderValue(string key) {
		try {
			foreach (Transform transform in trModUnit[key]) {
				if (transform.name == "SliderUnit") {
					var slider = FindChildByTag(transform, "Slider").GetComponent<UISlider>();
					var prop = GetTag(slider, 2);

					_modsParam.fValue[key][prop] = _modsParam.fVdef[key][prop];

					SetSliderValue(slider, key);
				}
			}
		} catch (Exception ex) {
			Debug.Log($"{LogLabel}ResetSliderValue() {ex}");
		}
	}

	private void SetSliderValue(UISlider slider, string key) {
		var prop = GetTag(slider, 2);

		slider.value = CodecSliderValue(key, prop);

		var text = $"{CodecSliderValue(key, prop, slider.value):F2}";
		_uiValueLabels[key][prop].text = text;
		_uiValueLabels[key][prop].gameObject.GetComponent<UIInput>().value = text;

		//Debug.LogWarning($"{key}#{prop} = {_modsParam.fValue[key][prop]}");
	}


	private static readonly Dictionary<string, int> GridSortOrder = new() {
		{"System", -1},
		{"Unit", 0},
		{"Panel", 1},
		{"Header", 2},
		{"Slider", 3},
		{"Spacer", 4}
	};

	private int SortGrid(Transform t1, Transform t2) {
		try {
			var tName1 = t1.name.Split(':');
			var tName2 = t2.name.Split(':');

			var type1 = tName1[0];
			var type2 = tName2[0];
			var key1 = tName1[1];
			var key2 = tName2[1];
			var key1Pos = _modsParam.sKey.IndexOf(key1);
			var key2Pos = _modsParam.sKey.IndexOf(key2);

			//Debug.Log(t1.name +" comp "+ t2.name);

			if (key1Pos == key2Pos) {
				if (type1 == "Slider" && type2 == "Slider") {
					var l = Array.IndexOf(_modsParam.sPropName[key1], tName1[2]);
					var k = Array.IndexOf(_modsParam.sPropName[key2], tName2[2]);

					return l - k;
				} else {
					return GridSortOrder[type1] - GridSortOrder[type2];
				}
			} else {
				return key1Pos - key2Pos;
			}
		} catch (Exception ex) {
			Debug.Log($"{LogLabel}sortGridByXMLOrder() {ex}");
			return 0;
		}
	}

	private void SetSliderVisible(string key, bool enable) {
		foreach (Transform transform in trModUnit[key]) {
			var type = GetTag(transform, 0);
			if (type == "SliderUnit" || type == "Spacer") transform.gameObject.SetActive(enable);
		}

		_uiTable.repositionNow = true;
	}

	private void SetButtonColor(string key, bool b) {
		SetButtonColor(FindChild(trModUnit[key], "Header:" + key).GetComponent<UIButton>(), b);
	}

	private void SetButtonColor(UIButton button, bool enable) {
		var color = button.defaultColor;

		if (_modsParam.IsToggle(GetTag(button, 1))) {
			button.defaultColor = new(color.r, color.g, color.b, enable ? 1f : 0.5f);
			FindChild(button.gameObject, "SelectCursor").SetActive(enable);
		} else {
			button.defaultColor = new(color.r, color.g, color.b, enable ? 1f : 0.75f);
		}
	}

	private void WindowTweenFinished() {
		_uiScrollPanel.gameObject.SetActive(true);
	}

	private string GetTag(Component component, int n) => GetTag(component.gameObject, n);

	private string GetTag(GameObject gameObject, int n) {
		return (gameObject.name.Split(':') != null) ? gameObject.name.Split(':')[n] : "";
	}

	private float CodecSliderValue(string key, string prop) {
		var value = _modsParam.fValue[key][prop];
		var minValue = _modsParam.fVmin[key][prop];
		var maxValue = _modsParam.fVmax[key][prop];
		var valueType = _modsParam.sVType[key][prop];

		Math.Max(value, minValue);
		Math.Min(value, maxValue);

		if (valueType == "scale" && minValue < 1f) {
			Math.Max(minValue, 0f);
			Math.Max(value, 0f);

			return (value < 1f) ? (value - minValue) / (1f - minValue) * 0.5f : 0.5f + (value - 1f) / (maxValue - 1f) * 0.5f;
		} else if (valueType == "int") {
			var minValueDecimal = (decimal)minValue;

			return (float)Math.Round(((decimal)value - minValueDecimal) / ((decimal)maxValue - minValueDecimal), 1, MidpointRounding.AwayFromZero);
		} else {
			return (value - minValue) / (maxValue - minValue);
		}
	}

	private float CodecSliderValue(string key, string prop, float value) {
		var minValue = _modsParam.fVmin[key][prop];
		var maxValue = _modsParam.fVmax[key][prop];
		var valueType = _modsParam.sVType[key][prop];

		Math.Max(value, 0f);
		Math.Min(value, 1f);

		if (valueType == "scale" && minValue < 1f) {
			Math.Max(minValue, 0f);
			Math.Max(value, 0f);

			return (value < 0.5f) ? minValue + (1f - minValue) * value * 2f : 1 + (maxValue - 1f) * (value - 0.5f) * 2;
		} else if (valueType == "int") {
			return (float)Math.Round(minValue + (maxValue - minValue) * value, 0, MidpointRounding.AwayFromZero);
		} else {
			return minValue + (maxValue - minValue) * value;
		}
	}


	//--------

	private void NotifyMaidVoicePitchOnChange() {
		gameObject.SendMessage("MaidVoicePitch_UpdateSliders");
	}

	public void syncExSaveDatatoSlider() {
		Debug.Log(LogLabel + "Loading ExternalPresetData...");
		Debug.Log("----------------ExternalPresetData----------------");
		GetExternalSaveData();
		Debug.Log("------------------------------------------------");
		try {
			foreach (var key in _modsParam.sKey) {
				foreach (Transform transform in trModUnit[key]) {
					if (_modsParam.IsToggle(key)) {
						SetButtonColor(key, _modsParam.bEnabled[key]);
					}

					if (_modsParam.IsSlider(key)) {
						if (transform.name == "SliderUnit") {
							var slider = FindChildByTag(transform, "Slider").GetComponent<UISlider>();
							var prop = GetTag(slider, 2);

							slider.value = CodecSliderValue(key, prop);
							_uiValueLabels[key][prop].text = $"{CodecSliderValue(key, prop, slider.value):F2}";
							_uiValueLabels[key][prop].gameObject.GetComponent<UIInput>().value = _uiValueLabels[key][prop].text;
							//Debug.LogWarning($"{key}#{getTag(slider, 2)} = {_modsParam.fVdef[key][prop]}");
						}
					}
				}
			}
		} catch (Exception ex) {
			Debug.Log($"{LogLabel}syncExSaveDatatoSlider() {ex}");
		}
	}


	private void GetExternalSaveData() {
		foreach (var key in _modsParam.sKey) {
			_previousValues[key] = new();

			if (_modsParam.IsToggle(key)) {
				_modsParam.bEnabled[key] = ExSaveData.GetBool(_currentMaid, MaidVoicePitchPluginId, key, false);
				_previousValues[key]["enable"] = _modsParam.bEnabled[key] ? 1f : 0f;
				Debug.Log($"{key,-32} = {_modsParam.bEnabled[key],-16}");
			}

			if (_modsParam.IsSlider(key)) {
				foreach (var prop in _modsParam.sPropName[key]) {
					var f = ExSaveData.GetFloat(_currentMaid, MaidVoicePitchPluginId, prop, float.NaN);
					_modsParam.fValue[key][prop] = float.IsNaN(f) ? _modsParam.fVdef[key][prop] : f;
					_previousValues[key][prop] = _modsParam.fValue[key][prop];

					Debug.Log($"{prop,-32} = {_modsParam.fValue[key][prop]:f}");
				}
				if (!_modsParam.IsToggle(key)) {
					_modsParam.bEnabled[key] = true;
				}
			}
		}
	}

	private void SetExternalSaveData() {
		foreach (var key in _modsParam.sKey) {
			SetExternalSaveData(key);
		}
	}

	private void SetExternalSaveData(string key) {
		if (_modsParam.IsToggle(key)) {
			ExSaveData.SetBool(_currentMaid, MaidVoicePitchPluginId, key, _modsParam.bEnabled[key]);
		}

		if (_modsParam.IsSlider(key)) {
			foreach (var prop in _modsParam.sPropName[key]) {
				SetExternalSaveData(key, prop);
			}
		}
	}

	private void SetExternalSaveData(string key, string prop) {
		var value = (float)Math.Round(_modsParam.fValue[key][prop], 3, MidpointRounding.AwayFromZero);

		ExSaveData.SetFloat(_currentMaid, MaidVoicePitchPluginId, prop, value);
	}

	#endregion


	#region Utility methods

	internal static Transform FindParent(Transform transform, string name) => FindParent(transform.gameObject, name).transform;

	internal static GameObject FindParent(GameObject gameObject, string name) {
		if (gameObject == null) {
			return null;
		}

		var parent = gameObject.transform.parent;
		while (parent) {
			if (parent.name == name) {
				return parent.gameObject;
			}
			parent = parent.parent;
		}

		return null;
	}

	internal static Transform FindChild(Transform transform, string name) => FindChild(transform.gameObject, name).transform;

	internal static GameObject FindChild(GameObject gameObject, string name) {
		if (gameObject == null) {
			return null;
		}

		GameObject target = null;

		foreach (Transform transform in gameObject.transform) {
			if (transform.gameObject.name == name) {
				return transform.gameObject;
			}

			target = FindChild(transform.gameObject, name);
			if (target) {
				return target;
			}
		}

		return null;
	}

	internal static Transform FindChildByTag(Transform transform, string tag) => FindChildByTag(transform.gameObject, tag).transform;

	internal static GameObject FindChildByTag(GameObject gameObject, string tag) {
		if (gameObject == null) {
			return null;
		}

		GameObject target = null;

		foreach (Transform transform in gameObject.transform) {
			if (transform.gameObject.name.Contains(tag)) {
				return transform.gameObject;
			}

			target = FindChild(transform.gameObject, tag);
			if (target) {
				return target;
			}
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
		var clone = Instantiate(orignal);
		if (!clone) {
			return null;
		}

		clone.name = name;
		SetChild(parent, clone);

		return clone;
	}

	internal static void ReleaseChild(GameObject child) {
		child.transform.parent = null;
		child.SetActive(false);
	}

	internal static void DestroyChild(GameObject parent, string name) {
		var child = FindChild(parent, name);
		if (child) {
			child.transform.parent = null;
			Destroy(child);
		}
	}

	internal static UIAtlas FindAtlas(string name) {
		return new List<UIAtlas>(Resources.FindObjectsOfTypeAll<UIAtlas>()).FirstOrDefault(a => a.name == name);
	}

	internal static void WriteTransform(string name) {
		var gameObject = GameObject.Find(name);
		if (!gameObject) {
			return;
		}

		WriteTransform(gameObject.transform, 0, null);
	}

	internal static void WriteTransform(Transform transform) => WriteTransform(transform, 0, null);

	internal static void WriteTransform(Transform transform, int level, StreamWriter writer) {
		if (level == 0) {
			writer = new($".\\{transform.name}.txt", false);
		}

		if (writer == null) {
			return;
		}

		var indentation = new string(' ', 4 * Math.Max(0, level));

		writer.WriteLine(indentation + level + "," + transform.name);
		foreach (Transform transformChild in transform) {
			WriteTransform(transformChild, level + 1, writer);
		}

		if (level == 0) {
			writer.Close();
		}
	}

	internal static void WriteChildrenComponent(GameObject gameObject) {
		WriteComponent(gameObject);

		foreach (Transform transform in gameObject.transform) {
			WriteChildrenComponent(transform.gameObject);
		}
	}

	internal static void WriteComponent(GameObject gameObject) {
		foreach (var component in gameObject.GetComponents<Component>()) {
			Debug.Log($"{gameObject.name}:{component.GetType().Name}");
		}
	}

	#endregion

	#region Public methods
	public static void AddExternalModsParam(ExternalModsParam modsParam) {
		ExternalModParameters.Add(modsParam);
	}
	#endregion
}
