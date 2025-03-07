using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using CM3D2.ExternalPreset.Managed;
using CM3D2.ExternalSaveData.Managed;
using COM3D2.AddModsSlider;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CM3D2.AddModsSlider.Plugin;

[BepInPlugin("COM3D2.AddModsSlider", MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("COM3D2.ExternalSaveData")]
[BepInDependency("COM3D2.ExternalPresetData")]
public class AddModsSlider : BaseUnityPlugin {
	private const string MaidVoicePitchPluginId = "CM3D2.MaidVoicePitch";

	private const float TimePerInit = 0.10f;

	private const int UiRootWidth = 1920;
	private const int ScrollViewWidth = 550;
	private const int ScrollViewHeight = 860;

	private static ManualLogSource _logger;

	private bool _isInitialized = false;

	private ModParameters _modParameters;

	private Maid _currentMaid;

	private UIPanel _uiPanel;
	private UIPanel _uiScrollPanel;
	private UITable _uiTable;
	private Font _font;

	private readonly PluginSaveData _pluginSaveData = new(MaidVoicePitchPluginId);

	private readonly List<ModControl> _modControls = new();
	private readonly Dictionary<string, ModControl> _modControlsDictionary = new();

	internal static List<ExternalModsParam> ExternalModParameters { get; } = new();

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

	private void Awake() {
		_logger = Logger;
	}

	internal static void LogDebug(object data) {
		_logger.LogDebug(data);
	}

	internal static void LogError(object data) {
		_logger.LogError(data);
	}

	private void Start() {
		SceneManager.sceneLoaded += OnSceneLoaded;
		SceneManager.sceneUnloaded += OnSceneUnloaded;

		ExPreset.loadNotify.AddListener(syncExSaveDatatoSlider);
	}

	private void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
		if (scene.name == "SceneEdit") {
			_modParameters = new();
			if (_modParameters.Init()) {
				StartCoroutine(InitializeCoroutine());
			}
		}
	}

	private void OnSceneUnloaded(Scene scene) {
		if (scene.name == "SceneEdit") {
			_isInitialized = false;
			_modParameters = null;

			_currentMaid = null;

			_modControls.Clear();
			_modControlsDictionary.Clear();
		}
	}

	public void Update() {
		if (SceneManager.GetActiveScene().name == "SceneEdit" && _isInitialized && Input.GetKeyDown(KeyCode.F5)) {
			_uiPanel.gameObject.SetActive(!_uiPanel.gameObject.activeSelf);
		}
	}

	#endregion

	#region Callbacks

	public void OnClickHeaderButton() {
		var key = GetTag(UIButton.current, 1);
		var enabled = false;

		var parameter = _modParameters.GetParameter(key);

		if (parameter.IsToggle()) {
			enabled = !parameter.Enabled;
			parameter.Enabled = enabled;
			SetExternalSaveData(key);

			NotifyMaidVoicePitchOnChange();

			// WIDESLIDER有効化/無効化に合わせて、依存項目UIを表示/非表示
			if (key == ModParameters.WideSliderId) {
				toggleActiveOnWideSlider();
			}
		}

		if (parameter.IsSlider()) {
			if (!parameter.IsToggle()) {
				enabled = UIButton.current.defaultColor.a != 1f;
			}

			SetSliderVisible(key, enabled);
		}

		SetButtonColor(UIButton.current, enabled);
	}

	public void OnClickUndoAll() {
		foreach (var parameter in _modParameters.Parameters) {
			var key = parameter.Name;

			if (parameter.IsToggle()) {
				parameter.Enabled = parameter.WasEnabled;
				SetExternalSaveData(key);
				SetButtonColor(key, parameter.Enabled);
			}

			if (parameter.IsSlider()) {
				UndoSliderValue(key);
				SetExternalSaveData(key);

				if (parameter.IsToggle()) {
					SetSliderVisible(key, parameter.Enabled);
				}
			}
		}

		NotifyMaidVoicePitchOnChange();
		toggleActiveOnWideSlider();
	}

	public void OnClickUndoButton() {
		var key = GetTag(UIButton.current, 1);
		UndoSliderValue(key);
		SetExternalSaveData(key);
		NotifyMaidVoicePitchOnChange();
	}

	public void OnClickResetAll() {
		foreach (var parameter in _modParameters.Parameters) {
			var key = parameter.Name;

			if (parameter.IsToggle()) {
				// everything except WideSlider defaults to disabled
				parameter.Enabled = key == ModParameters.WideSliderId;
				SetExternalSaveData(key);
				SetButtonColor(key, parameter.Enabled);
			}

			if (parameter.IsSlider()) {
				ResetSliderValue(key);
				SetExternalSaveData(key);

				if (parameter.IsToggle()) {
					SetSliderVisible(key, parameter.Enabled);
				}
			}
		}

		NotifyMaidVoicePitchOnChange();
		toggleActiveOnWideSlider();
	}

	public void OnClickResetButton() {
		var key = GetTag(UIButton.current, 1);
		ResetSliderValue(key);
		SetExternalSaveData(key);
		NotifyMaidVoicePitchOnChange();
	}

	public void OnChangeSlider() {
		var key = GetTag(UIProgressBar.current, 1);
		var prop = GetTag(UIProgressBar.current, 2);

		var modControl = _modControlsDictionary[key];

		var value = modControl.CodecSliderValue(prop, UIProgressBar.current.value);

		var text = value.ToString("F2");
		var label = modControl.Labels[prop];
		label.text = text;
		label.gameObject.GetComponent<UIInput>().value = text;

		modControl.Parameter.SetPropertyValue(prop, value);

		SetExternalSaveData(key, prop);

		NotifyMaidVoicePitchOnChange();
	}

	public void OnSubmitSliderValueInput() {
		var key = GetTag(UIInput.current, 1);
		var prop = GetTag(UIInput.current, 2);
		UISlider slider = null;

		foreach (Transform t in UIInput.current.transform.parent.parent) {
			if (GetTag(t, 0) == "Slider") {
				slider = t.GetComponent<UISlider>();
			}
		}

		var modControl = _modControlsDictionary[key];

		if (float.TryParse(UIInput.current.value, out var value)) {
			modControl.Parameter.SetPropertyValue(prop, value);

			slider.value = modControl.CodecSliderValue(prop);

			var text = modControl.CodecSliderValue(prop, slider.value).ToString("F2");
			UIInput.current.value = text;
			modControl.Labels[prop].text = text;
		}
	}

	#endregion

	#region Private methods

	private IEnumerator InitializeCoroutine() {
		while (!(_isInitialized = Initialize())) {
			yield return new WaitForSeconds(TimePerInit);
		}

		Logger.LogDebug("Initialization complete.");
	}

	private GameObject CreateSliderTemplate(GameObject uiRoot, UIAtlas uiAtlasSceneEdit) {
		var sliderTemplate = new GameObject("TestSliderUnit");

		SetChild(uiRoot, sliderTemplate);

		var uiTestSliderUnitFrame = sliderTemplate.AddComponent<UISprite>();
		uiTestSliderUnitFrame.atlas = uiAtlasSceneEdit;
		uiTestSliderUnitFrame.spriteName = "cm3d2_edit_slidertitleframe";
		uiTestSliderUnitFrame.type = UIBasicSprite.Type.Sliced;
		uiTestSliderUnitFrame.SetDimensions(500, 50);

		// スライダー作成
		var uiTestSlider = NGUITools.AddChild<UISlider>(sliderTemplate);
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
		var uiTestSliderLabel = NGUITools.AddChild<UILabel>(sliderTemplate);
		uiTestSliderLabel.name = "Label";
		uiTestSliderLabel.trueTypeFont = _font;
		uiTestSliderLabel.fontSize = 20;
		uiTestSliderLabel.text = "テストスライダー";
		uiTestSliderLabel.width = 110;
		uiTestSliderLabel.overflowMethod = UILabel.Overflow.ShrinkContent;

		uiTestSliderLabel.transform.localPosition = new(-190f, 0f, 0f);

		// 値ラベル・インプット作成
		var uiTestSliderValueBase = NGUITools.AddChild<UISprite>(sliderTemplate);
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

		uiTestSliderValueInput.gameObject.AddComponent<BoxCollider>();
		NGUITools.UpdateWidgetCollider(uiTestSliderValueInput.gameObject);

		sliderTemplate.SetActive(false);

		return sliderTemplate;
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

			// ボタンはgoProfileTabをコピー
			var profileTabCopy = Instantiate(FindChild(uiRoot.transform.Find("ProfilePanel").Find("Comment").gameObject, "ProfileTab"));
			EventDelegate.Remove(profileTabCopy.GetComponent<UIButton>().onClick, ProfileMgr.Instance.ChangeCommentTab);
			profileTabCopy.SetActive(false);

			var profileTabLabel = FindChild(profileTabCopy, "Name").GetComponent<UILabel>();
			_font = profileTabLabel.trueTypeFont;

			var sliderTemplate = CreateSliderTemplate(uiRoot, uiAtlasSceneEdit);

			#region createPanel

			// ModsSliderPanel作成
			var uiPanelPosition = new Vector3(UiRootWidth / 2f - 15f - ScrollViewWidth / 2f - 50f, 40f, 0f);
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
			uiScrollView.contentPivot = UIWidget.Pivot.Top;
			uiScrollView.movement = UIScrollView.Movement.Vertical;
			uiScrollView.dragEffect = UIScrollView.DragEffect.Momentum;
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
			uiLabelTitleTab.text = MyPluginInfo.PLUGIN_NAME;

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
			uiLabelUndoAll.color = Color.black;
			uiLabelUndoAll.text = "Undo all";

			var uiButtonUndoAll = goUndoAll.GetComponent<UIButton>();
			uiButtonUndoAll.defaultColor = new(1f, 1f, 1f, 0.8f);
			EventDelegate.Set(uiButtonUndoAll.onClick, OnClickUndoAll);

			FindChild(goUndoAll, "SelectCursor").GetComponent<UISprite>().SetDimensions(16, 16);
			FindChild(goUndoAll, "SelectCursor").SetActive(false);
			NGUITools.UpdateWidgetCollider(goUndoAll);
			goUndoAll.SetActive(true);

			// Resetボタン
			var goResetAll = SetCloneChild(goSystemUnit, goUndoAll, "ResetAll");
			goResetAll.transform.localPosition = new(controlWidth * 0.25f - 4, baseTop - systemUnitHeight / 2f, 0f);

			var uiLabelResetAll = FindChild(goResetAll, "Name").GetComponent<UILabel>();
			uiLabelResetAll.text = "Reset all";

			var uiButtonResetAll = goResetAll.GetComponent<UIButton>();
			uiButtonResetAll.defaultColor = new(1f, 1f, 1f, 0.8f);
			EventDelegate.Set(uiButtonResetAll.onClick, OnClickResetAll);

			NGUITools.UpdateWidgetCollider(goResetAll);
			goResetAll.SetActive(true);

			#endregion

			// 拡張セーブデータ読込
			LoadExternalSaveData();

			#region addTableContents

			// ModsParamの設定に従ってボタン・スライダー追加
			foreach (var parameter in _modParameters.Parameters) {
				var key = parameter.Name;

				if (!parameter.Visible) {
					continue;
				}

				var modControl = new ModControl();
				_modControls.Add(modControl);
				_modControlsDictionary.Add(key, modControl);

				modControl.Parameter = parameter;

				// ModUnit：modタグ単位のまとめオブジェクト ScrollViewGridの子
				var goModUnit = NGUITools.AddChild(_uiTable.gameObject);
				goModUnit.name = "Unit:" + key;
				modControl.ModUnit = goModUnit.transform;

				// プロフィールタブ複製・追加
				var goHeaderButton = SetCloneChild(goModUnit, profileTabCopy, "Header:" + key);
				goHeaderButton.SetActive(true);
				goHeaderButton.AddComponent<UIDragScrollView>().scrollView = uiScrollView;
				var uiHeaderButton = goHeaderButton.GetComponent<UIButton>();
				EventDelegate.Set(uiHeaderButton.onClick, OnClickHeaderButton);
				SetButtonColor(uiHeaderButton, parameter.IsToggle() && parameter.Enabled);

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
				uiLabelHeader.alignment = NGUIText.Alignment.Left;
				uiLabelHeader.multiLine = false;
				uiLabelHeader.overflowMethod = UILabel.Overflow.ClampContent;
				uiLabelHeader.color = Color.black;
				uiLabelHeader.supportEncoding = true;
				uiLabelHeader.text = $"{parameter.Description} [C0C0C0]({key})[-]";
				uiLabelHeader.gameObject.AddComponent<UIDragScrollView>().scrollView = uiScrollView;

				// 金枠Sprite
				var uiSpriteHeaderCursor = FindChild(goHeaderButton, "SelectCursor").GetComponent<UISprite>();
				uiSpriteHeaderCursor.gameObject.SetActive(parameter.IsToggle() && parameter.Enabled);

				NGUITools.UpdateWidgetCollider(goHeaderButton);

				// スライダーならUndo/Resetボタンとスライダー追加
				if (parameter.IsSlider()) {
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
					uiLabelUndo.color = Color.black;
					uiLabelUndo.text = "Undo";

					var uiButtonUndo = goUndo.GetComponent<UIButton>();
					uiButtonUndo.defaultColor = new(1f, 1f, 1f, 0.8f);

					EventDelegate.Set(uiButtonUndo.onClick, OnClickUndoButton);
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
					uiLabelReset.color = Color.black;
					uiLabelReset.text = "Reset";

					var uiButtonReset = goReset.GetComponent<UIButton>();
					uiButtonReset.defaultColor = new(1f, 1f, 1f, 0.8f);

					EventDelegate.Set(uiButtonReset.onClick, OnClickResetButton);
					FindChild(goReset, "SelectCursor").GetComponent<UISprite>().SetDimensions(16, 16);
					FindChild(goReset, "SelectCursor").SetActive(false);
					NGUITools.UpdateWidgetCollider(goReset);
					goReset.SetActive(true);

					for (var j = 0; j < parameter.PropertyNames.Count; j++) {
						var prop = parameter.PropertyNames[j];

						var property = parameter.Properties[prop];

						if (!property.Visible) {
							continue;
						}

						var value = property.Value;
						var minValue = property.MinValue;
						var maxValue = property.MaxValue;
						var label = property.Label;
						var valueType = property.Type;

						// スライダーをModUnitに追加
						var goSliderUnit = SetCloneChild(goModUnit, sliderTemplate, "SliderUnit");
						goSliderUnit.transform.localPosition = new(0f, j * -70f - uiSpriteHeaderButton.height - 20f, 0f);
						goSliderUnit.AddComponent<UIDragScrollView>().scrollView = uiScrollView;

						// フレームサイズ
						goSliderUnit.GetComponent<UISprite>().SetDimensions(controlWidth, 50);

						// スライダー設定
						var uiModSlider = FindChild(goSliderUnit, "Slider").GetComponent<UISlider>();
						uiModSlider.name = $"Slider:{key}:{prop}";
						uiModSlider.value = modControl.CodecSliderValue(prop);
						if (valueType == "int") {
							uiModSlider.numberOfSteps = (int)(maxValue - minValue + 1);
						}
						EventDelegate.Add(uiModSlider.onChange, OnChangeSlider);

						// スライダーラベル設定
						FindChild(goSliderUnit, "Label").GetComponent<UILabel>().text = label;
						FindChild(goSliderUnit, "Label").AddComponent<UIDragScrollView>().scrollView = uiScrollView;

						// スライダー値ラベル参照取得
						var goValueLabel = FindChild(goSliderUnit, "Value");
						goValueLabel.name = $"Value:{key}:{prop}";
						modControl.Labels[prop] = goValueLabel.GetComponent<UILabel>();
						modControl.Labels[prop].multiLine = false;
						EventDelegate.Set(goValueLabel.GetComponent<UIInput>().onSubmit, OnSubmitSliderValueInput);

						// スライダー有効状態設定
						//goSliderUnit.SetActive( !parameter.IsToggle() || parameter.Enabled && parameter.CheckWideSlider() );
						goSliderUnit.SetActive(false);
					}
				}

				// 金枠Sprite
				uiSpriteHeaderCursor.type = UIBasicSprite.Type.Sliced;
				uiSpriteHeaderCursor.SetDimensions(uiSpriteHeaderButton.width - 4, uiSpriteHeaderButton.height - 4);
			}

			#endregion

			_uiTable.Reposition();
			uiScrollView.ResetPosition();
			goUiPanel.SetActive(false);
			toggleActiveOnWideSlider();
		} catch (Exception ex) {
			Logger.LogError($"{nameof(Initialize)}() {ex}");
			return false;
		}

		return true;
	}

	public void toggleActiveOnWideSlider() => toggleActiveOnWideSlider(_modParameters.WideSliderIsEnabled());

	public void toggleActiveOnWideSlider(bool enable) {
		try {
			foreach (Transform transform in _uiTable.gameObject.transform) {
				var goType = GetTag(transform, 0);
				var goKey = GetTag(transform, 1);

				if (goType == "System") {
					continue;
				}

				var parameter = _modParameters.GetParameter(goKey);

				if (parameter.OnWideSlider) {
					var labelText = $"{parameter.Description} [C0C0C0]({goKey})[-]";
					if (!enable) {
						labelText = "[FF0000]WS必須[-] " + labelText;
					}
					transform.GetComponentsInChildren<UILabel>()[0].text = labelText;

					var uiButton = transform.GetComponentsInChildren<UIButton>()[0];
					uiButton.isEnabled = enable;
					if (!(enable && parameter.IsSlider())) {
						SetButtonColor(uiButton, enable && parameter.Enabled);
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
			Logger.LogError($"{nameof(toggleActiveOnWideSlider)}() {ex}");
		}
	}

	private void UndoSliderValue(string key) {
		var modControl = _modControlsDictionary[key];
		foreach (Transform transform in modControl.ModUnit) {
			if (transform.name == "SliderUnit") {
				var slider = FindChildByTag(transform, "Slider").GetComponent<UISlider>();
				var prop = GetTag(slider, 2);

				modControl.Parameter.UndoPropertyValue(prop);

				SetSliderValue(slider, modControl);
			}
		}
	}

	private void ResetSliderValue(string key) {
		var modControl = _modControlsDictionary[key];
		foreach (Transform transform in modControl.ModUnit) {
			if (transform.name == "SliderUnit") {
				var slider = FindChildByTag(transform, "Slider").GetComponent<UISlider>();
				var prop = GetTag(slider, 2);

				modControl.Parameter.ResetPropertyValue(prop);

				SetSliderValue(slider, modControl);
			}
		}
	}

	private void SetSliderValue(UISlider slider, ModControl modControl) {
		var prop = GetTag(slider, 2);

		slider.value = modControl.CodecSliderValue(prop);

		var text = modControl.CodecSliderValue(prop, slider.value).ToString("F2");
		var label = modControl.Labels[prop];
		label.text = text;
		label.gameObject.GetComponent<UIInput>().value = text;
	}

	private static readonly Dictionary<string, int> GridSortOrder = new() {
		["System"] = -1,
		["Unit"] = 0,
		["Panel"] = 1,
		["Header"] = 2,
		["Slider"] = 3,
		["Spacer"] = 4,
	};

	private int SortGrid(Transform t1, Transform t2) {
		try {
			var tName1 = t1.name.Split(':');
			var tName2 = t2.name.Split(':');

			var type1 = tName1[0];
			var type2 = tName2[0];
			var key1 = tName1[1];
			var key2 = tName2[1];

			var parameter1 = _modParameters.GetParameter(key1);
			var parameter2 = _modParameters.GetParameter(key2);

			var key1Pos = _modParameters.Parameters.IndexOf(parameter1);
			var key2Pos = _modParameters.Parameters.IndexOf(parameter2);

			if (key1Pos == key2Pos) {
				if (type1 == "Slider" && type2 == "Slider") {
					var l = parameter1.PropertyNames.IndexOf(tName1[2]);
					var k = parameter2.PropertyNames.IndexOf(tName2[2]);

					return l - k;
				} else {
					return GridSortOrder[type1] - GridSortOrder[type2];
				}
			} else {
				return key1Pos - key2Pos;
			}
		} catch (Exception ex) {
			Logger.LogError($"{nameof(SortGrid)}() {ex}");
			return 0;
		}
	}

	private void SetSliderVisible(string key, bool enable) {
		foreach (Transform transform in _modControlsDictionary[key].ModUnit) {
			var type = GetTag(transform, 0);
			if (type == "SliderUnit" || type == "Spacer") {
				transform.gameObject.SetActive(enable);
			}
		}

		// retain scroll offset from top, making headers "stay in place" when toggled
		var scrollView = _uiScrollPanel.gameObject.GetComponent<UIScrollView>();
		var preExtents = scrollView.bounds.extents;
		_uiTable.Reposition();
		var diff = scrollView.bounds.extents - preExtents;
		_uiScrollPanel.clipOffset += new Vector2(diff.x, diff.y);
		_uiScrollPanel.transform.localPosition -= diff;

		// fixes position if header at bottom was collapsed
		_uiTable.repositionNow = true;
	}

	private void SetButtonColor(string key, bool enabled) {
		SetButtonColor(FindChild(_modControlsDictionary[key].ModUnit, "Header:" + key).GetComponent<UIButton>(), enabled);
	}

	private void SetButtonColor(UIButton button, bool enabled) {
		var color = button.defaultColor;

		var parameter = _modParameters.GetParameter(GetTag(button, 1));
		if (parameter.IsToggle()) {
			button.defaultColor = new(color.r, color.g, color.b, enabled ? 1f : 0.5f);
			FindChild(button.gameObject, "SelectCursor").SetActive(enabled);
		} else {
			button.defaultColor = new(color.r, color.g, color.b, enabled ? 1f : 0.75f);
		}
	}

	private string GetTag(Component component, int n) => GetTag(component.gameObject, n);

	private string GetTag(GameObject gameObject, int n) {
		return (gameObject.name.Split(':') != null) ? gameObject.name.Split(':')[n] : "";
	}

	private void NotifyMaidVoicePitchOnChange() {
		gameObject.SendMessage("MaidVoicePitch_UpdateSliders");
	}

	public void syncExSaveDatatoSlider() {
		LoadExternalSaveData();

		try {
			foreach (var modControl in _modControls) {
				var parameter = modControl.Parameter;
				var key = parameter.Name;

				foreach (Transform transform in modControl.ModUnit) {
					if (parameter.IsToggle()) {
						SetButtonColor(key, parameter.Enabled);
					}

					if (parameter.IsSlider()) {
						if (transform.name == "SliderUnit") {
							var slider = FindChildByTag(transform, "Slider").GetComponent<UISlider>();
							SetSliderValue(slider, modControl);
						}
					}
				}
			}

			toggleActiveOnWideSlider();
		} catch (Exception ex) {
			Logger.LogError($"{nameof(syncExSaveDatatoSlider)}() {ex}");
		}
	}

	private void LoadExternalSaveData() {
		Logger.LogDebug("Loading ExternalSaveData...");

		var maidData = _pluginSaveData.GetMaidData(_currentMaid);

		foreach (var parameter in _modParameters.Parameters) {
			if (parameter.IsToggle()) {
				parameter.Enabled = maidData.GetBoolean(parameter.Name, parameter.Name == ModParameters.WideSliderId);
				if (parameter.Name == ModParameters.WideSliderId) {
					// inform ExSaveData backend of WideSlider's state
					maidData.SetBoolean(parameter.Name, parameter.Enabled);
				}
				parameter.WasEnabled = parameter.Enabled;
			}

			if (parameter.IsSlider()) {
				foreach (var prop in parameter.PropertyNames) {
					var property = parameter.Properties[prop];
					var f = maidData.GetFloat(prop, float.NaN);
					property.Value = float.IsNaN(f) ? property.DefaultValue : f;
					property.PreviousValue = property.Value;
				}

				if (!parameter.IsToggle()) {
					parameter.Enabled = true;
				}
			}
		}
	}

	private void SetExternalSaveData() {
		foreach (var parameter in _modParameters.Parameters) {
			SetExternalSaveData(parameter.Name);
		}
	}

	private void SetExternalSaveData(string key) {
		var parameter = _modParameters.GetParameter(key);

		if (parameter.IsToggle()) {
			var maidData = _pluginSaveData.GetMaidData(_currentMaid);
			maidData.SetBoolean(parameter.Name, parameter.Enabled);
		}

		if (parameter.IsSlider()) {
			foreach (var prop in parameter.PropertyNames) {
				SetExternalSaveData(parameter.Name, prop);
			}
		}
	}

	private void SetExternalSaveData(string key, string prop) {
		var property = _modParameters.GetParameter(key).Properties[prop];
		var value = (float)Math.Round(property.Value, 3, MidpointRounding.AwayFromZero);

		var maidData = _pluginSaveData.GetMaidData(_currentMaid);
		maidData.SetFloat(prop, value);
	}

	#endregion

	#region Utility methods

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

	internal static UIAtlas FindAtlas(string name) {
		return new List<UIAtlas>(Resources.FindObjectsOfTypeAll<UIAtlas>()).FirstOrDefault(a => a.name == name);
	}

	#endregion

	public static void AddExternalModsParam(ExternalModsParam modsParam) {
		ExternalModParameters.Add(modsParam);
	}

	class ModControl {
		public Transform ModUnit { get; internal set; }
		public Dictionary<string, UILabel> Labels { get; set; } = new();
		public ModParameters.Parameter Parameter { get; internal set; }

		public float CodecSliderValue(string prop) {
			var property = Parameter.Properties[prop];
			var value = property.Value;
			var minValue = property.MinValue;
			var maxValue = property.MaxValue;
			var propertyType = property.Type;

			Math.Max(value, minValue);
			Math.Min(value, maxValue);

			if (propertyType == "scale" && minValue < 1f) {
				Math.Max(minValue, 0f);
				Math.Max(value, 0f);

				return (value < 1f) ? (value - minValue) / (1f - minValue) * 0.5f : 0.5f + (value - 1f) / (maxValue - 1f) * 0.5f;
			} else if (propertyType == "int") {
				var minValueDecimal = (decimal)minValue;

				return (float)Math.Round(((decimal)value - minValueDecimal) / ((decimal)maxValue - minValueDecimal), 1, MidpointRounding.AwayFromZero);
			} else {
				return (value - minValue) / (maxValue - minValue);
			}
		}

		public float CodecSliderValue(string prop, float value) {
			var property = Parameter.Properties[prop];
			var minValue = property.MinValue;
			var maxValue = property.MaxValue;
			var propertyType = property.Type;

			Math.Max(value, 0f);
			Math.Min(value, 1f);

			if (propertyType == "scale" && minValue < 1f) {
				Math.Max(minValue, 0f);
				Math.Max(value, 0f);

				return (value < 0.5f) ? minValue + (1f - minValue) * value * 2f : 1 + (maxValue - 1f) * (value - 0.5f) * 2;
			} else if (propertyType == "int") {
				return (float)Math.Round(minValue + (maxValue - minValue) * value, 0, MidpointRounding.AwayFromZero);
			} else {
				return minValue + (maxValue - minValue) * value;
			}
		}
	}
}
