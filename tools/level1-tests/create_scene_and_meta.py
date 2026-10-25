"""Author the checked-in scene and fill missing stable Unity meta files. Never replace existing GUIDs."""
from pathlib import Path
import uuid

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / 'unity/Assets'
NS = uuid.UUID('e4ab686a-e658-4729-859e-f0e674066e11')

def guid(path):
    meta = Path(str(path) + '.meta')
    if meta.exists():
        for line in meta.read_text().splitlines():
            if line.startswith('guid: '): return line.split()[1]
    return uuid.uuid5(NS, path.relative_to(ROOT).as_posix()).hex

scene = ASSETS / 'Scenes/MainGameScene.unity'
select_scene = ASSETS / 'Scenes/LevelSelectScene.unity'
controller = ASSETS / 'Level1/Runtime/Level1Game.cs'
select_controller = ASSETS / 'Level1/Runtime/LevelSelectScreen.cs'
# Camera and offline bootstrap are serialized scene objects. No external package MonoBehaviours.
# Both scenes share the SAME serialized shape: one controller GameObject (which builds its own
# Canvas/UI/input in Awake) plus a serialized Main Camera + AudioListener.
SCENE_TEMPLATE = ('''%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1 &100
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  serializedVersion: 6
  m_Component:
  - component: {fileID: 101}
  - component: {fileID: 102}
  m_Layer: 0
  m_Name: SCENE_ROOT_NAME
  m_TagString: Untagged
  m_Icon: {fileID: 0}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &101
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 100}
  serializedVersion: 2
  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
  m_LocalPosition: {x: 0, y: 0, z: 0}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {fileID: 0}
  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}
--- !u!114 &102
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 100}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: CONTROLLER_GUID, type: 3}
  m_Name:
  m_EditorClassIdentifier:
--- !u!1 &200
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  serializedVersion: 6
  m_Component:
  - component: {fileID: 201}
  - component: {fileID: 202}
  - component: {fileID: 203}
  m_Layer: 0
  m_Name: Main Camera
  m_TagString: MainCamera
  m_Icon: {fileID: 0}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &201
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 200}
  serializedVersion: 2
  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
  m_LocalPosition: {x: 0, y: 0, z: -10}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {fileID: 0}
  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}
--- !u!20 &202
Camera:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 200}
  m_Enabled: 1
  serializedVersion: 2
  m_ClearFlags: 2
  m_BackGroundColor: {r: 0.0627451, g: 0.1803922, b: 0.2313726, a: 1}
  m_projectionMatrixMode: 1
  m_GateFitMode: 2
  m_FOVAxisMode: 0
  m_Iso: 200
  m_ShutterSpeed: 0.005
  m_Aperture: 16
  m_FocusDistance: 10
  m_FocalLength: 50
  m_BladeCount: 5
  m_Curvature: {x: 2, y: 11}
  m_BarrelClipping: 0.25
  m_Anamorphism: 0
  m_SensorSize: {x: 36, y: 24}
  m_LensShift: {x: 0, y: 0}
  m_NormalizedViewPortRect:
    serializedVersion: 2
    x: 0
    y: 0
    width: 1
    height: 1
  near clip plane: 0.3
  far clip plane: 100
  field of view: 60
  orthographic: 1
  orthographic size: 5
  m_Depth: -1
  m_CullingMask:
    serializedVersion: 2
    m_Bits: 4294967295
  m_RenderingPath: -1
  m_TargetTexture: {fileID: 0}
  m_TargetDisplay: 0
  m_TargetEye: 3
  m_HDR: 1
  m_AllowMSAA: 1
  m_AllowDynamicResolution: 0
  m_ForceIntoRT: 0
  m_OcclusionCulling: 1
  m_StereoConvergence: 10
  m_StereoSeparation: 0.022
--- !u!81 &203
AudioListener:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 200}
  m_Enabled: 1
--- !u!1660057539 &9223372036854775807
SceneRoots:
  m_ObjectHideFlags: 0
  m_Roots:
  - {fileID: 101}
  - {fileID: 201}
''')
scene.write_text(SCENE_TEMPLATE.replace('CONTROLLER_GUID', guid(controller)).replace('SCENE_ROOT_NAME', 'Tideleaf Island - Offline Level 1'))
select_scene.write_text(SCENE_TEMPLATE.replace('CONTROLLER_GUID', guid(select_controller)).replace('SCENE_ROOT_NAME', 'Level Select - Offline Menu'))

for path in sorted(ASSETS.rglob('*')):
    if path.suffix == '.meta' or path.name.startswith('.'): continue
    meta = Path(str(path) + '.meta')
    if meta.exists(): continue
    text = 'fileFormatVersion: 2\nguid: ' + guid(path) + '\n'
    if path.is_dir(): text += 'folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n'
    elif path.suffix == '.cs':
        text += 'MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n'
    elif path.suffix == '.asmdef': text += 'AssemblyDefinitionImporter:\n  externalObjects: {}\n'
    elif path.suffix == '.png':
        text += '''TextureImporter:
  internalIDToNameTable: []
  externalObjects: {}
  serializedVersion: 12
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
  isReadable: 0
  textureFormat: 1
  maxTextureSize: 1024
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  textureType: 0
  textureShape: 1
  alphaSource: 1
  alphaIsTransparency: 1
  spriteMode: 0
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 1024
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
'''
    elif path.suffix == '.ttf':
        text += '''TrueTypeFontImporter:
  externalObjects: {}
  serializedVersion: 4
  fontSize: 18
  forceTextureCase: -2
  characterSpacing: 0
  characterPadding: 1
  includeFontData: 1
  fontName:
  fontNames: []
  fallbackFontReferences: []
  customCharacters:
  fontRenderingMode: 0
  ascentCalculationMode: 1
  useLegacyBoundsCalculation: 0
  shouldRoundAdvanceValue: 1
'''
    elif path.suffix == '.txt': text += 'TextScriptImporter:\n  externalObjects: {}\n'
    else: text += 'DefaultImporter:\n  externalObjects: {}\n'
    text += '  userData:\n  assetBundleName:\n  assetBundleVariant:\n'
    meta.write_text(text)

build = ROOT / 'unity/ProjectSettings/EditorBuildSettings.asset'
# Level Select boots first (scene 0); the shared game scene is scene 1.
build.write_text('''%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1045 &1
EditorBuildSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_Scenes:
  - enabled: 1
    path: Assets/Scenes/LevelSelectScene.unity
    guid: SELECT_GUID
  - enabled: 1
    path: Assets/Scenes/MainGameScene.unity
    guid: MAIN_GUID
  m_configObjects: {}
'''.replace('SELECT_GUID', guid(select_scene)).replace('MAIN_GUID', guid(scene)))
print('Scenes authored (MainGameScene + LevelSelectScene); build list set; missing meta filled without changing existing GUIDs.')
