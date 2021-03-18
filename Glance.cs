// TODO: Change width ratio
// TODO: Vertical / Horizontal offset (look down)
// TODO: Look at empties?
// TODO: Player, look down
// TODO: Snap when looking away, still apply randomize (e.g. random spots in the frustrum)
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SimpleJSON;
using UnityEngine;
using Random = UnityEngine.Random;

public class Glance : MVRScript
{
    private const float _mirrorScanSpan = 0.5f;
    private const float _objectScanSpan = 0.08f;
    private const float _validateExtremesSpan = 0.04f;
    private const float _naturalLookDistance = 0.8f;
    private const float _angularVelocityPredictiveMultiplier = 0.5f;

    private static readonly HashSet<string> _mirrorAtomTypes = new HashSet<string>(new[]
    {
        "Glass",
        "Glass-Stained",
        "ReflectiveSlate",
        "ReflectiveWoodPanel",
    });

    private readonly JSONStorableBool _mirrorsJSON = new JSONStorableBool("Mirrors", true);
    private readonly JSONStorableFloat _playerEyesWeightJSON = new JSONStorableFloat("PlayerEyesWeight", 1f, 0f, 1f, true);
    private readonly JSONStorableFloat _playerMouthWeightJSON = new JSONStorableFloat("PlayerMouthWeight", 0.05f, 0f, 1f, true);
    private readonly JSONStorableFloat _windowCameraWeightJSON = new JSONStorableFloat("WindowCameraWeight", 1f, 0f, 1f, true);
    private readonly JSONStorableFloat _selfHandsWeightJSON = new JSONStorableFloat("SelfHandsWeight", 0f, 0f, 1f, true);
    private readonly JSONStorableFloat _selfGenitalsWeightJSON = new JSONStorableFloat("SelfGenitalsWeight", 1f, 0f, 1f, true);
    private readonly JSONStorableFloat _personsEyesWeightJSON = new JSONStorableFloat("PersonsEyesWeight", 1f, 0f, 1f, true);
    private readonly JSONStorableFloat _personsMouthWeightJSON = new JSONStorableFloat("PersonsMouthWeight", 0.05f, 0f, 1f, true);
    private readonly JSONStorableFloat _personsChestWeightJSON = new JSONStorableFloat("PersonsChestWeight", 0f, 0f, 1f, true);
    private readonly JSONStorableFloat _personsNipplesWeightJSON = new JSONStorableFloat("PersonsNipplesWeight", 0f, 0f, 1f, true);
    private readonly JSONStorableFloat _personsHandsWeightJSON = new JSONStorableFloat("PersonsHandsWeight", 0.05f, 0f, 1f, true);
    private readonly JSONStorableFloat _personsGenitalsWeightJSON = new JSONStorableFloat("PersonsGenitalsWeight", 0.5f, 0f, 1f, true);
    private readonly JSONStorableFloat _personsFeetWeightJSON = new JSONStorableFloat("PersonsFeetWeight", 0f, 0f, 1f, true);
    private readonly JSONStorableFloat _objectsWeightJSON = new JSONStorableFloat("ObjectsWeight", 0f, 0f, 1f, true);
    private readonly JSONStorableFloat _nothingWeightJSON = new JSONStorableFloat("NothingWeight", 0f, 0f, 1f, true);
    private readonly JSONStorableFloat _frustrumJSON = new JSONStorableFloat("FrustrumFOV", 20f, 0f, 45f, true);
    private readonly JSONStorableFloat _frustrumRatioJSON = new JSONStorableFloat("FrustrumRatio", 1.4f, 0.5f, 2f, true);
    private readonly JSONStorableFloat _frustrumRotateJSON = new JSONStorableFloat("FrustrumRotate", -5f, -45f, 45f, true);
    private readonly JSONStorableFloat _frustrumNearJSON = new JSONStorableFloat("FrustrumNear", 0.1f, 0f, 5f, false);
    private readonly JSONStorableFloat _frustrumFarJSON = new JSONStorableFloat("FrustrumFar", 5f, 0f, 5f, false);
    private readonly JSONStorableFloat _gazeMinDurationJSON = new JSONStorableFloat("GazeMinDuration", 0.5f, 0f, 10f, false);
    private readonly JSONStorableFloat _gazeMaxDurationJSON = new JSONStorableFloat("GazeMaxDuration", 2f, 0f, 10f, false);
    private readonly JSONStorableFloat _shakeMinDurationJSON = new JSONStorableFloat("ShakeMinDuration", 0.2f, 0f, 1f, false);
    private readonly JSONStorableFloat _shakeMaxDurationJSON = new JSONStorableFloat("ShakeMaxDuration", 0.5f, 0f, 1f, false);
    private readonly JSONStorableFloat _shakeRangeJSON = new JSONStorableFloat("ShakeRange", 0.015f, 0f, 0.1f, true);
    private readonly JSONStorableFloat _quickTurnThresholdJSON = new JSONStorableFloat("QuickTurnThreshold", 3f, 0f, 10f, false);
    private readonly JSONStorableFloat _quickTurnCooldownJSON = new JSONStorableFloat("QuickTurnCooldown", 0.5f, 0f, 2f, false);
    private readonly JSONStorableFloat _blinkSpaceMinJSON = new JSONStorableFloat("BlinkTimeMin", 1f, 0f, 10f, false);
    private readonly JSONStorableFloat _blinkSpaceMaxJSON = new JSONStorableFloat("BlinkTimeMax", 7f, 0f, 10f, false);
    private readonly JSONStorableFloat _blinkTimeMinJSON = new JSONStorableFloat("BlinkTimeMin", 0.1f, 0f, 2f, false);
    private readonly JSONStorableFloat _blinkTimeMaxJSON = new JSONStorableFloat("BlinkTimeMax", 0.4f, 0f, 2f, false);
    private readonly JSONStorableFloat _cameraMouthDistanceJSON = new JSONStorableFloat("CameraMouthEyesDistance", 0.053f, 0f, 0.1f, false);
    private readonly JSONStorableFloat _cameraEyesDistanceJSON = new JSONStorableFloat("CameraEyesDistance", 0.015f, 0f, 0.1f, false);
    private readonly JSONStorableBool _debugJSON = new JSONStorableBool("Debug", false);
    private readonly JSONStorableString _debugDisplayJSON = new JSONStorableString("DebugDisplay", "");

    private bool _ready;
    private bool _restored;
    private DAZBone[] _bones;
    private EyesControl _eyeBehavior;
    private DAZMeshEyelidControl _eyelidBehavior;
    private Transform _head;
    private Transform _lEye;
    private LookAtWithLimits _lEyeLimits;
    private LookAtWithLimits _rEyeLimits;
    private Transform _rEye;
    private Rigidbody _headRB;
    private FreeControllerV3 _eyeTarget;
    private Quaternion _frustrumRotation = Quaternion.Euler(-5f, 0f, 0f);
    private readonly List<BoxCollider> _mirrors = new List<BoxCollider>();
    private readonly List<EyeTargetReference> _objects = new List<EyeTargetReference>();
    private Vector3 _eyeTargetRestorePosition;
    private EyesControl.LookMode _eyeBehaviorRestoreLookMode;
    private bool _blinkRestoreEnabled;
    private readonly Plane[] _frustrumPlanes = new Plane[6];
    private readonly List<EyeTargetReference> _lockTargetCandidates = new List<EyeTargetReference>();
    private float _lockTargetCandidatesScoreSum;
    private float _nextMirrorScanTime;
    private BoxCollider _lookAtMirror;
    private float _lookAtMirrorDistance;
    private float _nextObjectsScanTime;
    private float _nextValidateExtremesTime;
    private float _nextLockTargetTime;
    private Transform _lockTarget;
    private float _nextShakeTime;
    private Vector3 _shakeValue;
    private float _nextGazeTime;
    private Vector3 _gazeTarget;
    private float _angularVelocityBurstCooldown;
    private readonly StringBuilder _debugDisplaySb = new StringBuilder();
    private LineRenderer _lineRenderer;
    private Vector3[] _lineRendererPoints;
    private Transform _cameraMouth;
    private Transform _cameraLEye;
    private Transform _cameraREye;

    public override void Init()
    {
        if (containingAtom.type != "Person")
        {
            enabled = false;
            return;
        }

        try
        {
            _eyeBehavior = (EyesControl) containingAtom.GetStorableByID("Eyes");
            _eyelidBehavior = (DAZMeshEyelidControl) containingAtom.GetStorableByID("EyelidControl");
            _bones = containingAtom.transform.Find("rescale2").GetComponentsInChildren<DAZBone>();
            _head = _bones.First(eye => eye.name == "head").transform;
            var lEyeBone = _bones.First(eye => eye.name == "lEye");
            _lEye = lEyeBone.transform;
            _lEyeLimits = lEyeBone.GetComponent<LookAtWithLimits>();
            var rEyeBone = _bones.First(eye => eye.name == "rEye");
            _rEye = rEyeBone.transform;
            _rEyeLimits = rEyeBone.GetComponent<LookAtWithLimits>();
            _headRB = _head.GetComponent<Rigidbody>();
            _eyeTarget = containingAtom.freeControllers.First(fc => fc.name == "eyeTargetControl");

            CreateToggle(_mirrorsJSON).label = "Mirrors (look at themselves)";
            CreateSlider(_playerEyesWeightJSON, false, "Eyes (you)", "F4");
            CreateSlider(_playerMouthWeightJSON, false, "Mouth (you)", "F4");
            CreateSlider(_windowCameraWeightJSON, false, "Window camera", "F4");
            CreateSlider(_selfHandsWeightJSON, false, "Hands (self)", "F4");
            CreateSlider(_selfGenitalsWeightJSON, false, "Genitals (self)", "F4");
            CreateSlider(_personsEyesWeightJSON , false, "Eyes (others)", "F4");
            CreateSlider(_personsMouthWeightJSON , false, "Mouth (others)", "F4");
            CreateSlider(_personsChestWeightJSON , false, "Chest (others)", "F4");
            CreateSlider(_personsNipplesWeightJSON , false, "Nipples (others)", "F4");
            CreateSlider(_personsHandsWeightJSON , false, "Hands (others)", "F4");
            CreateSlider(_personsGenitalsWeightJSON , false, "Genitals (others)", "F4");
            CreateSlider(_personsFeetWeightJSON , false, "Feet (others)", "F4");
            CreateSlider(_objectsWeightJSON, false, "Objects (toys, cua, shapes)", "F4");
            CreateSlider(_nothingWeightJSON, false, "Nothing (spacey)", "F4");

            CreateToggle(_debugJSON).label = "Show debug information";
            CreateTextField(_debugDisplayJSON);

            var presetsJSON = new JSONStorableStringChooser("Presets", new List<string>
            {
                ""
            }, "", "Apply preset");
            CreateScrollablePopup(presetsJSON, true);
            presetsJSON.setCallbackFunction = val =>
            {
                ResetToDefaults();
            };

            CreateSlider(_frustrumJSON, true, "Frustrum field of view", "F3");
            CreateSlider(_frustrumRatioJSON, true, "Frustrum ratio (multiply width)", "F3");
            CreateSlider(_frustrumRotateJSON, true, "Frustrum rotation (tilt)", "F3");
            CreateSlider(_frustrumNearJSON, true, "Frustrum near (closest)", "F3");
            CreateSlider(_frustrumFarJSON, true, "Frustrum far (furthest)", "F3");
            CreateSlider(_gazeMinDurationJSON, true, "Min target lock time", "F3");
            CreateSlider(_gazeMaxDurationJSON, true, "Max target lock time", "F3");
            CreateSlider(_shakeMinDurationJSON, true, "Min eye saccade time", "F4");
            CreateSlider(_shakeMaxDurationJSON, true, "Max eye saccade time", "F4");
            CreateSlider(_shakeRangeJSON, true, "Range of eye saccade", "F4");
            CreateSlider(_quickTurnThresholdJSON, true, "Quick turn threshold", "F3");
            CreateSlider(_quickTurnCooldownJSON, true, "Quick turn cooldown", "F3");
            CreateSlider(_blinkSpaceMinJSON, true, "Blink space min", "F2");
            CreateSlider(_blinkSpaceMaxJSON, true, "Blink space max", "F3");
            CreateSlider(_blinkTimeMinJSON, true, "Blink time min", "F4");
            CreateSlider(_blinkTimeMaxJSON, true, "Blink time max", "F4");
            CreateSlider(_cameraMouthDistanceJSON, true, "Camera mouth distance", "F4");
            CreateSlider(_cameraEyesDistanceJSON, true, "Camera eyes distance", "F4");

            RegisterBool(_mirrorsJSON);
            RegisterFloat(_playerEyesWeightJSON);
            RegisterFloat(_playerMouthWeightJSON);
            RegisterFloat(_windowCameraWeightJSON);
            RegisterFloat(_selfHandsWeightJSON);
            RegisterFloat(_selfGenitalsWeightJSON);
            RegisterFloat(_personsEyesWeightJSON );
            RegisterFloat(_personsMouthWeightJSON );
            RegisterFloat(_personsChestWeightJSON );
            RegisterFloat(_personsNipplesWeightJSON );
            RegisterFloat(_personsHandsWeightJSON );
            RegisterFloat(_personsGenitalsWeightJSON );
            RegisterFloat(_personsFeetWeightJSON );
            RegisterFloat(_objectsWeightJSON);
            RegisterFloat(_nothingWeightJSON);
            RegisterFloat(_frustrumJSON);
            RegisterFloat(_frustrumRatioJSON);
            RegisterFloat(_frustrumRotateJSON);
            RegisterFloat(_frustrumNearJSON);
            RegisterFloat(_frustrumFarJSON);
            RegisterFloat(_gazeMinDurationJSON);
            RegisterFloat(_gazeMaxDurationJSON);
            RegisterFloat(_shakeMinDurationJSON);
            RegisterFloat(_shakeMaxDurationJSON);
            RegisterFloat(_shakeRangeJSON);
            RegisterFloat(_quickTurnThresholdJSON);
            RegisterFloat(_quickTurnCooldownJSON);
            RegisterFloat(_blinkSpaceMinJSON);
            RegisterFloat(_blinkSpaceMaxJSON);
            RegisterFloat(_blinkTimeMinJSON);
            RegisterFloat(_blinkTimeMaxJSON);
            RegisterFloat(_cameraMouthDistanceJSON);
            RegisterFloat(_cameraEyesDistanceJSON);

            _mirrorsJSON.setCallbackFunction = _ => { if (enabled) Rescan(); };
            _playerEyesWeightJSON.setCallbackFunction = _ => { if (enabled) Rescan(); };
            _playerMouthWeightJSON.setCallbackFunction = _ => { if (enabled) Rescan(); };
            _windowCameraWeightJSON.setCallbackFunction = _ => { if (enabled) Rescan(); };
            _selfHandsWeightJSON.setCallbackFunction = _ => { if (enabled) Rescan(); };
            _selfGenitalsWeightJSON.setCallbackFunction = _ => { if (enabled) Rescan(); };
            _personsEyesWeightJSON.setCallbackFunction = _ => { if (enabled) Rescan(); };
            _personsMouthWeightJSON.setCallbackFunction = _ => { if (enabled) Rescan(); };
            _personsChestWeightJSON.setCallbackFunction = _ => { if (enabled) Rescan(); };
            _personsNipplesWeightJSON.setCallbackFunction = _ => { if (enabled) Rescan(); };
            _personsHandsWeightJSON.setCallbackFunction = _ => { if (enabled) Rescan(); };
            _personsGenitalsWeightJSON.setCallbackFunction = _ => { if (enabled) Rescan(); };
            _personsFeetWeightJSON.setCallbackFunction = _ => { if (enabled) Rescan(); };
            _objectsWeightJSON.setCallbackFunction = _ => { if (enabled) Rescan(); };
            _nothingWeightJSON.setCallbackFunction = _ => { if (enabled) Rescan(); };
            _frustrumRotateJSON.setCallbackFunction = val => _frustrumRotation = Quaternion.Euler(_frustrumRotateJSON.val, 0f, 0f);
            _frustrumNearJSON.setCallbackFunction = val => _frustrumFarJSON.valNoCallback = Mathf.Max(val, _frustrumFarJSON.val);
            _frustrumFarJSON.setCallbackFunction = val => _frustrumNearJSON.valNoCallback = Mathf.Min(val, _frustrumNearJSON.val);
            _gazeMinDurationJSON.setCallbackFunction = val => _gazeMaxDurationJSON.valNoCallback = Mathf.Max(val, _gazeMaxDurationJSON.val);
            _gazeMaxDurationJSON.setCallbackFunction = val => _gazeMinDurationJSON.valNoCallback = Mathf.Min(val, _gazeMinDurationJSON.val);
            _shakeMinDurationJSON.setCallbackFunction = val => _shakeMaxDurationJSON.valNoCallback = Mathf.Max(val, _shakeMaxDurationJSON.val);
            _shakeMaxDurationJSON.setCallbackFunction = val => _shakeMinDurationJSON.valNoCallback = Mathf.Min(val, _shakeMinDurationJSON.val);
            _blinkSpaceMinJSON.setCallbackFunction = val => _eyelidBehavior.blinkSpaceMin = val;
            _blinkSpaceMaxJSON.setCallbackFunction = val => _eyelidBehavior.blinkSpaceMax = val;
            _blinkTimeMinJSON.setCallbackFunction = val => _eyelidBehavior.blinkTimeMin = val;
            _blinkTimeMaxJSON.setCallbackFunction = val => _eyelidBehavior.blinkTimeMax = val;
            _cameraMouthDistanceJSON.setCallbackFunction = _ => { if (_cameraMouth != null) _cameraMouth.localPosition = new Vector3(0, -_cameraMouthDistanceJSON.val, 0); };
            _cameraEyesDistanceJSON.setCallbackFunction = _ => { if (_cameraMouth != null) { _cameraLEye.localPosition = new Vector3(-_cameraEyesDistanceJSON.val, 0, 0); _cameraREye.localPosition = new Vector3(_cameraEyesDistanceJSON.val, 0, 0); } };
            _debugJSON.setCallbackFunction = SyncLineRenderer;

            SuperController.singleton.StartCoroutine(DeferredInit());
        }
        catch (Exception e)
        {
            SuperController.LogError($"{nameof(Glance)}.{nameof(Init)}: {e}");
            enabled = false;
        }
    }

    private void ResetToDefaults()
    {
        _mirrorsJSON.SetValToDefault();
        _playerEyesWeightJSON.SetValToDefault();
        _playerMouthWeightJSON.SetValToDefault();
        _windowCameraWeightJSON.SetValToDefault();
        _selfHandsWeightJSON.SetValToDefault();
        _selfGenitalsWeightJSON.SetValToDefault();
        _personsEyesWeightJSON .SetValToDefault();
        _personsMouthWeightJSON .SetValToDefault();
        _personsChestWeightJSON .SetValToDefault();
        _personsNipplesWeightJSON .SetValToDefault();
        _personsHandsWeightJSON .SetValToDefault();
        _personsGenitalsWeightJSON .SetValToDefault();
        _personsFeetWeightJSON .SetValToDefault();
        _objectsWeightJSON.SetValToDefault();
        _nothingWeightJSON.SetValToDefault();
        _frustrumJSON.SetValToDefault();
        _frustrumRatioJSON.SetValToDefault();
        _frustrumRotateJSON.SetValToDefault();
        _frustrumNearJSON.SetValToDefault();
        _frustrumFarJSON.SetValToDefault();
        _gazeMinDurationJSON.SetValToDefault();
        _gazeMaxDurationJSON.SetValToDefault();
        _shakeMinDurationJSON.SetValToDefault();
        _shakeMaxDurationJSON.SetValToDefault();
        _shakeRangeJSON.SetValToDefault();
        _quickTurnThresholdJSON.SetValToDefault();
        _quickTurnCooldownJSON.SetValToDefault();
        _cameraMouthDistanceJSON.SetValToDefault();
        _cameraEyesDistanceJSON.SetValToDefault();
        _blinkSpaceMinJSON.SetValToDefault();
        _blinkSpaceMaxJSON.SetValToDefault();
        _blinkTimeMinJSON.SetValToDefault();
        _blinkTimeMaxJSON.SetValToDefault();
    }

    private void CreateSlider(JSONStorableFloat jsf, bool right, string label, string valueFormat = "F2")
    {
        var slider = CreateSlider(jsf, right);
        slider.label = label;
        slider.valueFormat = valueFormat;
    }

    private void SyncLineRenderer(bool val)
    {
        var exists = _lineRenderer != null;
        if (!val)
        {
            if (exists) Destroy(_lineRenderer.gameObject);
            _lineRendererPoints = null;
            return;
        }
        if (exists) return;
        var go = new GameObject("Gaze_LineRenderer");
        _lineRenderer = go.AddComponent<LineRenderer>();
        _lineRenderer.useWorldSpace = true;
        _lineRenderer.material = new Material(Shader.Find("Sprites/Default")) {renderQueue = 4000};
        _lineRenderer.colorGradient = new Gradient
        {
            colorKeys = new[] {new GradientColorKey(Color.cyan, 0f), new GradientColorKey(Color.cyan, 1f)}
        };
        _lineRenderer.widthMultiplier = 0.0004f;
        _lineRenderer.positionCount = 16;
        _lineRendererPoints = new Vector3[16];
    }

    private IEnumerator DeferredInit()
    {
        yield return new WaitForEndOfFrame();
        if (!_restored)
            containingAtom.RestoreFromLast(this);
        _ready = true;
        if (enabled)
            OnEnable();
    }

    public void OnEnable()
    {
        if (!_ready) return;

        try
        {
            var camera = SuperController.singleton.centerCameraTarget.transform;

            _cameraMouth = new GameObject("Glance_CameraMouth").transform;
            _cameraMouth.SetParent(camera, false);
            _cameraMouth.localPosition = new Vector3(0, -_cameraMouthDistanceJSON.val, 0);

            _cameraLEye = new GameObject("Glance_CameraLEye").transform;
            _cameraLEye.SetParent(camera, false);
            _cameraLEye.localPosition = new Vector3(-_cameraEyesDistanceJSON.val, 0, 0);

            _cameraREye = new GameObject("Glance_CameraREye").transform;
            _cameraREye.SetParent(camera, false);
            _cameraREye.localPosition = new Vector3(_cameraEyesDistanceJSON.val, 0, 0);

            _eyeTargetRestorePosition = _eyeTarget.control.position;
            _eyeBehaviorRestoreLookMode = _eyeBehavior.currentLookMode;
            _eyeBehavior.currentLookMode = EyesControl.LookMode.Target;

            _blinkRestoreEnabled = _eyelidBehavior.GetBoolParamValue("blinkEnabled");
            _eyelidBehavior.SetBoolParamValue("blinkEnabled", true);

            SuperController.singleton.onAtomUIDsChangedHandlers += ONAtomUIDsChanged;

            Rescan();
        }
        catch (Exception e)
        {
            SuperController.LogError($"{nameof(Glance)}.{nameof(OnEnable)}: {e}");
            enabled = false;
        }
    }

    public void OnDisable()
    {
        try
        {
            _debugJSON.val = false;

            if (_cameraMouth != null) Destroy(_cameraMouth.gameObject);
            if (_cameraLEye != null) Destroy(_cameraLEye.gameObject);
            if (_cameraREye != null) Destroy(_cameraREye.gameObject);

            SuperController.singleton.onAtomUIDsChangedHandlers -= ONAtomUIDsChanged;

            _eyeTarget.control.position = _eyeTargetRestorePosition;
            if (_eyeBehavior.currentLookMode != EyesControl.LookMode.Target)
                _eyeBehavior.currentLookMode = _eyeBehaviorRestoreLookMode;
            _eyelidBehavior.SetBoolParamValue("blinkEnabled", _blinkRestoreEnabled);

            _eyelidBehavior.blinkSpaceMin = 1f;
            _eyelidBehavior.blinkSpaceMax = 7f;
            _eyelidBehavior.blinkTimeMin = 0.1f;
            _eyelidBehavior.blinkTimeMax = 0.4f;

            ClearState();
        }
        catch (Exception e)
        {
            SuperController.LogError($"{nameof(Glance)}.{nameof(OnDisable)}: {e}");
        }
    }

    private void SyncMirrors()
    {
        _mirrors.Clear();

        if (!_mirrorsJSON.val) return;

        _mirrors.AddRange(SuperController.singleton.GetAtoms()
            .Where(a => _mirrorAtomTypes.Contains(a.type))
            .Where(a => a.on)
            .Select(a => a.GetComponentInChildren<BoxCollider>())
            .Where(c => c != null));
    }

    private void SyncObjects()
    {
        _objects.Clear();

        if (_playerEyesWeightJSON.val >= 0.01f)
        {
            _objects.Add(new EyeTargetReference(_cameraLEye, _playerEyesWeightJSON.val / 2f));
            _objects.Add(new EyeTargetReference(_cameraREye, _playerEyesWeightJSON.val / 2f));
        }

        if (_playerMouthWeightJSON.val >= 0.01f)
            _objects.Add(new EyeTargetReference(_cameraMouth, _playerMouthWeightJSON.val));

        foreach (var atom in SuperController.singleton.GetAtoms())
        {
            if (!atom.on) continue;

            switch (atom.type)
            {
                case "WindowCamera":
                {
                    if (_windowCameraWeightJSON.val < 0.01f) continue;
                    if (atom.GetStorableByID("CameraControl")?.GetBoolParamValue("cameraOn") != true) continue;
                    _objects.Add(new EyeTargetReference(atom.mainController.control, _windowCameraWeightJSON.val));
                    break;
                }
                case "Person":
                {
                    if (atom == containingAtom)
                    {
                        foreach (var bone in _bones)
                        {
                            if (_selfHandsWeightJSON.val >= 0.01f && (bone.name == "lHand" || bone.name == "rHand"))
                                _objects.Add(new EyeTargetReference(bone.transform, _selfHandsWeightJSON.val));
                            else if (_selfGenitalsWeightJSON.val >= 0.01f && (bone.name == "Gen1" || bone.name == "Gen3"))
                                _objects.Add(new EyeTargetReference(bone.transform, _selfGenitalsWeightJSON.val));
                        }

                        continue;
                    }

                    var bones = atom.transform.Find("rescale2").GetComponentsInChildren<DAZBone>();
                    if (_personsEyesWeightJSON.val > 0.01f)
                    {
                        _objects.Add(new EyeTargetReference(bones.First(b => b.name == "lEye").transform, _personsEyesWeightJSON.val / 2f));
                        _objects.Add(new EyeTargetReference(bones.First(b => b.name == "rEye").transform, _personsEyesWeightJSON.val / 2f));
                    }
                    if (_personsMouthWeightJSON.val > 0.01f)
                    {
                        _objects.Add(new EyeTargetReference(bones.First(b => b.name == "tongue03").transform, _personsMouthWeightJSON.val));
                    }
                    if (_personsChestWeightJSON.val > 0.01f)
                    {
                        _objects.Add(new EyeTargetReference(bones.First(b => b.name == "chest").transform, _personsChestWeightJSON.val));
                    }
                    if (_personsNipplesWeightJSON.val > 0.01f)
                    {
                        _objects.Add(new EyeTargetReference(atom.rigidbodies.First(b => b.name == "lNipple").transform, _personsNipplesWeightJSON.val / 2f));
                        _objects.Add(new EyeTargetReference(atom.rigidbodies.First(b => b.name == "rNipple").transform, _personsNipplesWeightJSON.val / 2f));
                    }
                    if (_personsHandsWeightJSON.val > 0.01f)
                    {
                        _objects.Add(new EyeTargetReference(bones.First(b => b.name == "lHand").transform, _personsHandsWeightJSON.val));
                        _objects.Add(new EyeTargetReference(bones.First(b => b.name == "rHand").transform, _personsHandsWeightJSON.val));
                    }
                    if (_personsGenitalsWeightJSON.val > 0.01f)
                    {
                        var selector = atom.GetComponentInChildren<DAZCharacterSelector>();
                        if (selector.selectedCharacter.isMale)
                        {
                            _objects.Add(new EyeTargetReference(bones.First(b => b.name == "Gen3").transform, _personsGenitalsWeightJSON.val * 0.8f));
                            _objects.Add(new EyeTargetReference(bones.First(b => b.name == "Testes").transform, _personsGenitalsWeightJSON.val * 0.2f));
                        }
                        else
                        {
                            _objects.Add(new EyeTargetReference(bones.First(b => b.name == "hip").transform, _personsGenitalsWeightJSON.val));
                        }
                    }
                    if (_personsFeetWeightJSON.val > 0.01f)
                    {
                        _objects.Add(new EyeTargetReference(bones.First(b => b.name == "lFoot").transform, _personsFeetWeightJSON.val / 2f));
                        _objects.Add(new EyeTargetReference(bones.First(b => b.name == "rFoot").transform, _personsFeetWeightJSON.val / 2f));
                    }

                    break;
                }
                case "Cube":
                case "Sphere":
                case "Dildo":
                case "Paddle":
                case "ToyAH":
                case "ToyBP":
                case "CustomUnityAsset":
                case "Torch":
                {
                    if (_objectsWeightJSON.val < 0.01f) continue;
                    _objects.Add(new EyeTargetReference(atom.mainController.control, _objectsWeightJSON.val));
                    break;
                }
                case "Empty":
                {
                    if (!atom.storeId.StartsWith("GlanceTarget_")) continue;
                        _objects.Add(new EyeTargetReference(atom.mainController.control));
                    break;
                }
            }
        }
    }

    public void Rescan()
    {
        ClearState();
        SyncMirrors();
        SyncObjects();
    }

    private void ClearState()
    {
        _lookAtMirror = null;
        _mirrors.Clear();
        _objects.Clear();
        _lockTargetCandidates.Clear();
        _lockTargetCandidatesScoreSum = 0f;
        _nextMirrorScanTime = 0f;
        _nextObjectsScanTime = 0f;
        _nextValidateExtremesTime = 0f;
        _nextLockTargetTime = 0f;
        _nextShakeTime = 0f;
        _shakeValue = Vector3.zero;
        _nextGazeTime = 0f;
        _gazeTarget = Vector3.zero;
        _angularVelocityBurstCooldown = 0f;
    }

    public void Update()
    {
        var eyesCenter = (_lEye.position + _rEye.position) / 2f;

        ScanMirrors(eyesCenter);
        ScanObjects(eyesCenter);
        InvalidateExtremes();
        SelectLockTarget();
        SelectShake();

        if (!ReferenceEquals(_lockTarget, null))
        {
            _eyeTarget.control.position = _lockTarget.transform.position + _shakeValue;
            return;
        }

        if (!ReferenceEquals(_lookAtMirror, null))
        {
            var reflectPosition = ComputeMirrorLookback(eyesCenter);
            _eyeTarget.control.position = reflectPosition + _shakeValue;
            return;
        }

        SelectGazeTarget(eyesCenter);
        _eyeTarget.control.position = _gazeTarget + _shakeValue;
    }

    private void InvalidateExtremes()
    {
        if (_nextValidateExtremesTime > Time.time) return;
        _nextValidateExtremesTime = Time.time + _validateExtremesSpan;

        if (AreEyesInRange()) return;

        // TODO: Doesn't seem to be called in practice?
        _nextGazeTime = 0f;
        _nextLockTargetTime = 0f;
        _angularVelocityBurstCooldown = _quickTurnCooldownJSON.val;
    }

    private bool AreEyesInRange()
    {
        if (!IsEyeInRange(_lEye, _lEyeLimits)) return false;
        if (!IsEyeInRange(_rEye, _rEyeLimits)) return false;
        return true;
    }

    private bool IsEyeInRange(Transform eye, LookAtWithLimits limits)
    {
        var angles = eye.localEulerAngles;
        var y = angles.y;
        if (y < 180)
        {
            if (Mathf.Abs(y - limits.MaxRight) < 0.1f)
                return false;
        }
        else if (Mathf.Abs(360 - y - limits.MaxLeft) < 0.1f)
        {
            return false;
        }

        var x = angles.x;
        if (x < 180)
        {
            if (Mathf.Abs(x - limits.MaxDown) < 1f)
                return false;
        }
        else if (Mathf.Abs(360 - x - limits.MaxUp) < 1f)
        {
            return false;
        }

        return true;
    }

    private bool IsInAngleRange(Vector3 eyesCenter, Vector3 targetPosition)
    {
        var lookAngle = _head.InverseTransformDirection(targetPosition - eyesCenter);
        var yaw = Vector3.Angle(Vector3.ProjectOnPlane(lookAngle, Vector3.up), Vector3.forward);
        if (yaw > 26) return false;
        var pitch = Vector3.Angle(Vector3.ProjectOnPlane(lookAngle, Vector3.right), Vector3.forward);
        if (pitch > 20) return false;
        return true;
    }

    private Vector3 ComputeMirrorLookback(Vector3 eyesCenter)
    {
        var mirrorTransform = _lookAtMirror.transform;
        var mirrorPosition = mirrorTransform.position;
        var mirrorNormal = mirrorTransform.up;
        var plane = new Plane(mirrorNormal, mirrorPosition);
        var planePoint = plane.ClosestPointOnPlane(eyesCenter);
        var reflectPosition = planePoint - (eyesCenter - planePoint);
        return reflectPosition;
    }

    private void SelectGazeTarget(Vector3 eyesCenter)
    {
        // Immediate recompute if the head moves fast
        if (_angularVelocityBurstCooldown != 0)
        {
            if (_angularVelocityBurstCooldown > Time.time) return;
            _angularVelocityBurstCooldown = 0f;
        }

        if (_headRB.angularVelocity.sqrMagnitude > _quickTurnThresholdJSON.val)
        {
            _angularVelocityBurstCooldown = Time.time + _quickTurnCooldownJSON.val;
            _nextGazeTime = 0f;
            _eyelidBehavior.Blink();
        }

        if (_nextGazeTime > Time.time) return;
        _nextGazeTime = Time.time + Random.Range(_gazeMinDurationJSON.val, _gazeMaxDurationJSON.val);

        var localAngularVelocity = transform.InverseTransformDirection(_headRB.angularVelocity);
        var angularVelocity = Quaternion.Euler(localAngularVelocity * Mathf.Rad2Deg * _angularVelocityPredictiveMultiplier);

        _gazeTarget = eyesCenter + (_head.rotation * _frustrumRotation * angularVelocity * Vector3.forward) * _naturalLookDistance;
    }

    private void SelectShake()
    {
        if (_nextShakeTime > Time.time) return;
        _nextShakeTime = Time.time + Random.Range(_shakeMinDurationJSON.val, _shakeMaxDurationJSON.val);

        _shakeValue = Random.insideUnitSphere * _shakeRangeJSON.val;
    }

    private void SelectLockTarget()
    {
        if (_nextLockTargetTime > Time.time) return;

        if (_lockTargetCandidates.Count == 0)
        {
            _lockTarget = null;
            _nextLockTargetTime = float.PositiveInfinity;
        }
        else if(_lockTargetCandidates.Count == 1)
        {
            _lockTarget = _lockTargetCandidates[0].transform;
            _nextLockTargetTime = float.PositiveInfinity;
        }
        else
        {
            var lockRoll = Random.Range(0f, _lockTargetCandidatesScoreSum);
            var lockTarget = new EyeTargetReference(null, 0f);
            var sum = 0f;
            for (var i = 0; i < _lockTargetCandidates.Count; i++)
            {
                lockTarget = _lockTargetCandidates[i];
                sum += lockTarget.weight;
                if (lockRoll < sum) break;
            }
            _lockTarget = lockTarget.transform;
            var gazeDuration = (_gazeMaxDurationJSON.val - _gazeMinDurationJSON.val) * lockTarget.weight;
            _nextLockTargetTime = Time.time + Random.Range(_gazeMinDurationJSON.val, _gazeMinDurationJSON.val + gazeDuration);
        }

        if (_debugJSON.val && UITransform.gameObject.activeInHierarchy) UpdateDebugDisplay();

        _shakeValue = Vector3.zero;
        _nextShakeTime = Time.time + Random.Range(_shakeMinDurationJSON.val, _shakeMaxDurationJSON.val);
    }

    private void UpdateDebugDisplay()
    {
        _debugDisplaySb.Length = 0;

        _debugDisplaySb.Append(_lockTargetCandidates.Count);
        _debugDisplaySb.Append(" in focus over ");
        _debugDisplaySb.Append(_objects.Count);
        _debugDisplaySb.Append(" potential targets.");
        _debugDisplaySb.AppendLine();

        if (!ReferenceEquals(_lockTarget, null))
        {
            var fc = _lockTarget.GetComponent<FreeControllerV3>();
            if (!ReferenceEquals(fc, null))
            {
                _debugDisplaySb.Append("Locked on '");
                _debugDisplaySb.Append(fc.name);
                _debugDisplaySb.Append("' of atom '");
                _debugDisplaySb.Append(fc.containingAtom.name);
                _debugDisplaySb.AppendLine("'");
            }
            else
            {
                _debugDisplaySb.Append("Locked on '");
                _debugDisplaySb.Append(_lockTarget.name);
                _debugDisplaySb.AppendLine("'");
            }
        }
        else
        {
            _debugDisplaySb.AppendLine("Not locked on a target.");
        }

        _debugDisplayJSON.val = _debugDisplaySb.ToString();
        _debugDisplaySb.Length = 0;
    }

    private void ScanObjects(Vector3 eyesCenter)
    {
        if (_nextObjectsScanTime > Time.time) return;
        _nextObjectsScanTime = Time.time + _objectScanSpan;

        if (_objects.Count == 0) return;

        var originalCount = _lockTargetCandidates.Count;
        _lockTargetCandidates.Clear();
        _lockTargetCandidatesScoreSum = 0f;

        //var planes = GeometryUtility.CalculateFrustumPlanes(SuperController.singleton.centerCameraTarget.targetCamera);
        CalculateFrustum(eyesCenter, _head.rotation * _frustrumRotation * Vector3.forward, _frustrumJSON.val * Mathf.Deg2Rad, _frustrumRatioJSON.val, _frustrumNearJSON.val, _frustrumFarJSON.val, _frustrumPlanes);

        Transform closest = null;
        var closestDistance = float.PositiveInfinity;
        foreach (var o in _objects)
        {
            var position = o.transform.position;
            var bounds = new Bounds(position, new Vector3(0.001f, 0.001f, 0.001f));
            if (!GeometryUtility.TestPlanesAABB(_frustrumPlanes, bounds)) continue;
            var distance = Vector3.SqrMagnitude(bounds.center - eyesCenter);
            if (distance > _lookAtMirrorDistance) continue;
            if (!IsInAngleRange(eyesCenter, position)) continue;
            var score = o.weight - (distance / 10f);
            _lockTargetCandidates.Add(new EyeTargetReference(
                o.transform,
                score
            ));
            _lockTargetCandidatesScoreSum += score;
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = o.transform;
            }
        }

        if (_nothingWeightJSON.val > 0.01f)
        {
            _lockTargetCandidates.Add(new EyeTargetReference(
                null,
                _nothingWeightJSON.val
            ));
            _lockTargetCandidatesScoreSum += _nothingWeightJSON.val;
        }

        if (_lockTargetCandidates.Count != originalCount)
        {
            if (_lockTargetCandidates.Count > 0)
            {
                _lockTarget = closest;
                _nextLockTargetTime = Time.time + Random.Range(_gazeMinDurationJSON.val, _gazeMaxDurationJSON.val);
            }
            else
            {
                _nextLockTargetTime = 0;
                _nextGazeTime = 0;
            }
        }
    }

    private void ScanMirrors(Vector3 eyesCenter)
    {
        if (_nextMirrorScanTime > Time.time) return;
        _nextMirrorScanTime = Time.time + _mirrorScanSpan;

        _lookAtMirror = null;
        _lookAtMirrorDistance = float.PositiveInfinity;

        if (_mirrors.Count <= 0)
            return;

        var headPosition = _head.position;

        if (_mirrors.Count == 1)
        {
            _lookAtMirror = _mirrors[0];
            _lookAtMirrorDistance = Vector3.Distance(headPosition, _lookAtMirror.transform.position);
            return;
        }

        var ray = new Ray(eyesCenter, _head.forward);
        var closestMirrorDistance = float.PositiveInfinity;
        BoxCollider closestMirror = null;
        for (var i = 0; i < _mirrors.Count; i++)
        {
            var potentialMirror = _mirrors[i];
            var potentialMirrorDistance = Vector3.Distance(headPosition, potentialMirror.transform.position);
            if (potentialMirrorDistance < closestMirrorDistance)
            {
                closestMirrorDistance = potentialMirrorDistance;
                closestMirror = potentialMirror;
            }

            RaycastHit hit;
            if (!potentialMirror.Raycast(ray, out hit, 20f))
                continue;
            if (hit.distance > _lookAtMirrorDistance) continue;
            _lookAtMirrorDistance = hit.distance;
            _lookAtMirror = potentialMirror;
        }

        if (ReferenceEquals(_lookAtMirror, null))
        {
            if (ReferenceEquals(closestMirror, null)) return;
            _lookAtMirror = closestMirror;
        }
    }

    // Source: http://answers.unity.com/answers/1024526/view.html
    private void CalculateFrustum(Vector3 origin, Vector3 direction, float fovRadians, float viewRatio, float near, float far, Plane[] frustrumPlanes)
    {
        var nearCenter = origin + direction * near;
        var farCenter = origin + direction * far;
        var camRight = Vector3.Cross(direction, Vector3.up) * -1;
        var camUp = Vector3.Cross(direction, camRight);
        var nearHeight = 2 * Mathf.Tan(fovRadians / 2) * near;
        var farHeight = 2 * Mathf.Tan(fovRadians / 2) * far;
        var nearWidth = nearHeight * viewRatio;
        var farWidth = farHeight * viewRatio;
        var farTopLeft = farCenter + camUp * (farHeight * 0.5f) - camRight * (farWidth * 0.5f);
        var farBottomLeft = farCenter - camUp * (farHeight * 0.5f) - camRight * (farWidth * 0.5f);
        var farBottomRight = farCenter - camUp * (farHeight * 0.5f) + camRight * (farWidth * 0.5f);
        var nearTopLeft = nearCenter + camUp * (nearHeight * 0.5f) - camRight * (nearWidth * 0.5f);
        var nearTopRight = nearCenter + camUp * (nearHeight * 0.5f) + camRight * (nearWidth * 0.5f);
        var nearBottomRight = nearCenter - camUp * (nearHeight * 0.5f) + camRight * (nearWidth * 0.5f);
        frustrumPlanes[0] = new Plane(nearTopLeft, farTopLeft, farBottomLeft);
        frustrumPlanes[1] = new Plane(nearTopRight, nearBottomRight, farBottomRight);
        frustrumPlanes[2] = new Plane(farBottomLeft, farBottomRight, nearBottomRight);
        frustrumPlanes[3] = new Plane(farTopLeft, nearTopLeft, nearTopRight);
        frustrumPlanes[4] = new Plane(nearBottomRight, nearTopRight, nearTopLeft);
        frustrumPlanes[5] = new Plane(farBottomRight, farBottomLeft, farTopLeft);

        if (_lineRendererPoints != null)
        {
            //not needed; 6 points are sufficient to calculate the frustum
            var farTopRight = farCenter + camUp*(farHeight*0.5f) + camRight*(farWidth*0.5f);
            var nearBottomLeft  = nearCenter - camUp*(nearHeight*0.5f) - camRight*(nearWidth*0.5f);

            _lineRendererPoints[0] = nearTopLeft;
            _lineRendererPoints[1] = nearTopRight;
            _lineRendererPoints[2] = farTopRight;
            _lineRendererPoints[3] = nearTopRight;
            _lineRendererPoints[4] = nearBottomRight;
            _lineRendererPoints[5] = farBottomRight;
            _lineRendererPoints[6] = nearBottomRight;
            _lineRendererPoints[7] = nearBottomLeft;
            _lineRendererPoints[8] = farBottomLeft;
            _lineRendererPoints[9] = nearBottomLeft;
            _lineRendererPoints[10] = nearTopLeft;
            _lineRendererPoints[11] = farTopLeft;
            _lineRendererPoints[12] = farTopRight;
            _lineRendererPoints[13] = farBottomRight;
            _lineRendererPoints[14] = farBottomLeft;
            _lineRendererPoints[15] = farTopLeft;
            _lineRenderer.SetPositions(_lineRendererPoints);
        }
    }

    private void ONAtomUIDsChanged(List<string> uids)
    {
        Rescan();
    }

    public override void RestoreFromJSON(JSONClass jc, bool restorePhysical = true, bool restoreAppearance = true, JSONArray presetAtoms = null, bool setMissingToDefault = true)
    {
        base.RestoreFromJSON(jc, restorePhysical, restoreAppearance, presetAtoms, setMissingToDefault);
            _restored = true;
    }

    private struct EyeTargetReference
    {
        public Transform transform;
        public float weight;

        public EyeTargetReference(Transform transform, float weight = 1f)
        {
            this.transform = transform;
            this.weight = weight;
        }
    }
}
