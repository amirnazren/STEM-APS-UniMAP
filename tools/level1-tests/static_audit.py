"""Source/serialization audit, not a Unity import or Play Mode test. Run from any directory."""
from pathlib import Path
import hashlib
import json
import os
import re
import subprocess
import sys
import tempfile
import yaml
from PIL import Image
from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parents[2]
UNITY = ROOT / 'unity'
ASSETS = UNITY / 'Assets'
checks = []

def check(name, ok, detail):
    checks.append({'name': name, 'pass': bool(ok), 'detail': detail})
    print(('PASS ' if ok else 'FAIL ') + name + ': ' + detail)

def load_unity(path):
    text = path.read_text()
    text = re.sub(r'^%.*\n', '', text, flags=re.M)
    text = re.sub(r'^--- !u!\d+ &\d+', '---', text, flags=re.M)
    return list(yaml.safe_load_all(text))

def read(path): return (ROOT / path).read_text()

def owner(path, definitions):
    return next((definitions[p] for p in [path.parent, *path.parents] if p in definitions), None)

meta_map, bad_meta, missing_meta = {}, [], []
for path in sorted(ASSETS.rglob('*')):
    if any(p.startswith('.') for p in path.relative_to(ASSETS).parts): continue
    if path.suffix == '.meta':
        data = yaml.safe_load(path.read_text()); guid = data.get('guid')
        if not re.fullmatch(r'[a-f0-9]{32}', str(guid)) or guid in meta_map or not Path(str(path)[:-5]).exists():
            bad_meta.append(str(path.relative_to(ROOT)))
        meta_map[guid] = Path(str(path)[:-5])
    elif not Path(str(path) + '.meta').exists(): missing_meta.append(str(path.relative_to(ROOT)))
check('asset meta coverage and unique GUIDs', not missing_meta and not bad_meta,
      f'{len(meta_map)} meta files; missing={missing_meta}; malformed/duplicate/orphan={bad_meta}')

scene = ASSETS / 'Scenes/MainGameScene.unity'
text = scene.read_text(); ids = re.findall(r'^--- !u!\d+ &(\d+)', text, re.M)
objects = dict(zip(ids, load_unity(scene)))
check('scene YAML and unique fileIDs', len(ids) == len(set(ids)) and len(ids) == len(objects), f'{len(ids)} serialized objects')
errors = []
for ref in re.findall(r'\{fileID: [^}]+\}', text):
    data = yaml.safe_load(ref)
    if data.get('fileID', 0) == 0: continue
    if 'guid' in data:
        if data['guid'] not in meta_map: errors.append(ref)
        elif data['fileID'] != 11500000 or meta_map[data['guid']].suffix != '.cs': errors.append(ref)
    elif str(data['fileID']) not in objects: errors.append(ref)
check('scene local fileIDs and external script references', not errors, str(errors or 'All resolve to shipped objects / C# MonoScript asset'))
controller = objects['102']['MonoBehaviour']
script = meta_map[controller['m_Script']['guid']]
check('scene boot controller active and correctly typed', script.name == 'Level1Game.cs' and controller['m_Enabled'] == 1 and objects['100']['GameObject']['m_IsActive'] == 1,
      str(script.relative_to(ROOT)))
roots = objects['9223372036854775807']['SceneRoots']['m_Roots']
check('scene components and roots linked', all(str(r['fileID']) in objects for r in roots) and all(
    objects[str(c['component']['fileID'])][next(iter(objects[str(c['component']['fileID'])]))]['m_GameObject']['fileID'] == int(oid)
    for oid, ob in objects.items() if 'GameObject' in ob for c in ob['GameObject']['m_Component']), 'Component owners, root transforms and GameObjects agree')
check('camera and listener present', objects['200']['GameObject']['m_TagString'] == 'MainCamera' and objects['202']['Camera']['orthographic'] == 1 and objects['203']['AudioListener']['m_Enabled'] == 1, 'One serialized camera and AudioListener')
build = load_unity(UNITY / 'ProjectSettings/EditorBuildSettings.asset')[0]['EditorBuildSettings']['m_Scenes']
scene_guid = next(g for g,p in meta_map.items() if p == scene)
select = ASSETS / 'Scenes/LevelSelectScene.unity'
select_guid = next(g for g,p in meta_map.items() if p == select)
check('build scene list order (Level Select first, game second)',
      len(build) == 2
      and build[0]['enabled'] == 1 and build[0]['path'] == 'Assets/Scenes/LevelSelectScene.unity' and build[0]['guid'] == select_guid
      and build[1]['enabled'] == 1 and build[1]['path'] == 'Assets/Scenes/MainGameScene.unity' and build[1]['guid'] == scene_guid,
      'Scene 0 = LevelSelectScene, Scene 1 = MainGameScene; GUIDs match their meta')

# The first-booting scene (Level Select) must carry an enabled, correctly-typed controller + camera.
sel_text = select.read_text(); sel_ids = re.findall(r'^--- !u!\d+ &(\d+)', sel_text, re.M)
sel_objects = dict(zip(sel_ids, load_unity(select)))
sel_errors = []
for ref in re.findall(r'\{fileID: [^}]+\}', sel_text):
    data = yaml.safe_load(ref)
    if data.get('fileID', 0) == 0: continue
    if 'guid' in data:
        if data['guid'] not in meta_map or data['fileID'] != 11500000 or meta_map[data['guid']].suffix != '.cs': sel_errors.append(ref)
    elif str(data['fileID']) not in sel_objects: sel_errors.append(ref)
sel_controller = sel_objects['102']['MonoBehaviour']
sel_script = meta_map[sel_controller['m_Script']['guid']]
check('first scene (Level Select) boots cleanly with its own controller', not sel_errors
      and sel_script.name == 'LevelSelectScreen.cs' and sel_controller['m_Enabled'] == 1
      and sel_objects['100']['GameObject']['m_IsActive'] == 1
      and sel_objects['200']['GameObject']['m_TagString'] == 'MainCamera' and sel_objects['203']['AudioListener']['m_Enabled'] == 1,
      str(sel_script.relative_to(ROOT)) + '; refs=' + str(sel_errors or 'all resolve'))

bad_refs = []
for p in list(ASSETS.rglob('*.unity')) + list(ASSETS.rglob('*.asset')) + list((UNITY/'ProjectSettings').glob('*.asset')):
    load_unity(p)
    for guid in re.findall(r'guid: ([a-fA-F0-9]+)', p.read_text()):
        if guid in meta_map or guid in ('00000000000000000000000000000000','0000000000000000f000000000000000','0000000000000000e000000000000000'): continue
        bad_refs.append(f'{p.relative_to(ROOT)}: {guid}')
check('project settings YAML and asset GUID references', not bad_refs, str(bad_refs or 'Only shipped assets or Unity built-in resources'))

manifest = json.loads((UNITY/'Packages/manifest.json').read_text())['dependencies']
package_assemblies = {'UnityEngine.UI':'com.unity.ugui', 'Unity.TextMeshPro':'com.unity.textmeshpro',
    'Unity.RenderPipelines.Core.Runtime':'com.unity.render-pipelines.universal', 'Unity.RenderPipelines.Universal.Runtime':'com.unity.render-pipelines.universal'}
definitions = {p.parent:json.loads(p.read_text()) for p in ASSETS.rglob('*.asmdef')}
by_name = {d['name']:d for d in definitions.values()}
unresolved = [(d['name'],r) for d in definitions.values() for r in d.get('references',[]) if r not in by_name and (r not in package_assemblies or package_assemblies[r] not in manifest)]
check('all asmdef references resolved by project or declared packages', not unresolved and len(by_name) == len(definitions), str(unresolved or sorted(by_name)))
legacy = by_name['EcoBotBuilders.Legacy']
settings = load_unity(UNITY/'ProjectSettings/ProjectSettings.asset')[0]['PlayerSettings']
check('legacy networking excluded without dangling SocketIO assembly', legacy['defineConstraints'] == ['ECOBOT_LEGACY_ENABLED'] and legacy['autoReferenced'] is False and 'ECOBOT_LEGACY_ENABLED' not in str(settings['scriptingDefineSymbols']) and all('SocketIO' not in r for d in definitions.values() for r in d.get('references',[])), 'Opt-in legacy define absent; no SocketIO package required by active assemblies')
for name in ['EcoBotBuilders.Level1.Editor','EcoBotBuilders.Pipeline.Editor']:
    check(name + ' editor isolation', by_name[name]['includePlatforms'] == ['Editor'], 'Excluded from player builds')
runtime = by_name['EcoBotBuilders.Level1.Runtime']; core = by_name['EcoBotBuilders.Level1.Core']
check('runtime dependency boundary', runtime['references'] == ['EcoBotBuilders.Level1.Core','UnityEngine.UI'] and core['references'] == [] and core['noEngineReferences'], 'Unity-free model; runtime references Core/uGUI only')
active = [p for p in ASSETS.rglob('*.cs') if owner(p,definitions)['name'] != 'EcoBotBuilders.Legacy']
leaks = [str(p.relative_to(ROOT)) for p in active if owner(p,definitions)['includePlatforms'] != ['Editor'] and re.search(r'\b(UnityEditor|SocketIOClient|Newtonsoft|EcoBotBuilders\.Network)\b',p.read_text())]
check('active source has no legacy or editor dependency leak', not leaks, str(leaks or 'Inspected every active .cs ownership boundary'))
mismatches = []
for p in active:
    classes = re.findall(r'class\s+(\w+)\s*:\s*MonoBehaviour',p.read_text())
    if classes and classes != [p.stem]: mismatches.append(str(p.relative_to(ROOT)))
check('active MonoBehaviour filename mapping', not mismatches, str(mismatches or 'Each attached runtime MonoBehaviour has its own matching script file'))

boot = script.read_text()
input_axes = load_unity(UNITY/'ProjectSettings/InputManager.asset')[0]['InputManager']['m_Axes']
check('input and EventSystem configuration', settings['activeInputHandler'] == 0 and {'Horizontal','Vertical','Submit','Cancel'} <= {a['m_Name'] for a in input_axes} and 'typeof(StandaloneInputModule)' in boot and 'typeof(EventSystem)' in boot and 'typeof(GraphicRaycaster)' in boot, 'Legacy Input Manager selected explicitly; uGUI mouse/touch module and required axes wired')
check('auto-wiring source entry points', all(s in boot for s in ['void Awake()', 'EnsureCameraAndInput(); BuildUI(); RefreshProgram();','new IslandBoard(root,session.Level', 'AddComponent<IslandAudio>()','CanvasScaler.ScaleMode.ScaleWithScreenSize','Screen.safeArea']), 'Awake creates UI/board/audio/input; source check, not an execution claim')
check('Unity version and URP package pin', '2022.3.21f1' in (UNITY/'ProjectSettings/ProjectVersion.txt').read_text() and manifest.get('com.unity.render-pipelines.universal') == '14.0.10' and manifest.get('com.unity.ugui') == '1.0.0', 'Unity 2022.3.21f1 / URP 14.0.10 / uGUI 1.0.0; package resolution still requires real Editor import')
helper = (ASSETS/'Editor/URPBootstrap.cs').read_text()
check('URP helper API source review markers', all(s in helper for s in ['#if UNITY_EDITOR','using UnityEngine.Rendering;','ResourceReloader.TryReloadAllNullIn','UniversalRenderPipelineAsset.Create(renderer)','GraphicsSettings.defaultRenderPipeline']) and 'using UnityEditor.Rendering;' not in helper,
      'Public APIs checked against official Graphics 2022.3 source; explicit Core/Universal runtime assembly references; no Editor compilation claimed')

resources = {}
for p in ASSETS.rglob('*'):
    if not p.is_file() or p.suffix == '.meta' or 'Resources' not in p.parts: continue
    key = p.relative_to(next(parent for parent in p.parents if parent.name == 'Resources')).with_suffix('').as_posix()
    resources.setdefault(key,[]).append(p)
source = '\n'.join(p.read_text() for p in active)
requested = set(re.findall(r'Resources\.Load<[^>]+>\("([^"]+)"\)',source))
requested |= {'Level1/'+key for key in re.findall(r'IslandUI\.Art\("([^"]+)"',source)}
# IslandBoard draws its sprites from BoardTheme fields (variables, not literals), so also pull the
# themed sprite stems declared in LevelPresentation.cs — this covers the Level 2 dusty/clean panels.
presentation = (ASSETS/'Level1/Runtime/LevelPresentation.cs').read_text()
for field in ('Tile','Target','Cleaned','Obstacle'):
    requested |= {'Level1/'+m for m in re.findall(field+r'\s*=\s*"([^"]+)"', presentation)}
expected = {'Level1/Interface','Level1/InterfaceBold','Level1/robot','Level1/grass','Level1/litter',
            'Level1/stone','Level1/panel_dusty','Level1/panel_clean'}
check('all Resources texture and font lookups', all(len(resources.get(key,[])) == 1 for key in requested) and requested == expected,
      str(len(requested)) + ' assets: ' + ', '.join(sorted(requested)))
for key in sorted(requested):
    p = resources[key][0]
    if p.suffix == '.png':
        with Image.open(p) as im:
            im.load(); valid = im.size == (1024,1024) and im.mode == 'RGBA' and im.getextrema()[3][0] == 0
        importer = yaml.safe_load(Path(str(p)+'.meta').read_text())['TextureImporter']
        check(key+' image/import settings', valid and importer['alphaIsTransparency'] == 1 and importer['maxTextureSize'] <= 2048, 'Valid transparent 1024×1024 PNG, local texture importer')
    else:
        font = TTFont(p); cmap = font.getBestCmap(); font.close()
        check(key+' UI symbol coverage', all(ord(c) in cmap for c in '↑↓×−✓≡▶➜'), 'Bundled font covers control/heading symbols')

baseline = 'c7de4eb5be82a88ee16f2c9dcd83bd53ea3c1715'
# The Level 1 test HARNESS is still byte-identical to the baseline; Puzzle.cs changed (budget-aware),
# so instead of reusing old evidence we RE-RUN both suites fresh against the current shared model.
harness_unchanged = subprocess.check_output(['git','show',baseline+':tools/level1-tests/Tests.cs'],cwd=ROOT) == (ROOT/'tools/level1-tests/Tests.cs').read_bytes()
def run_suite(sources, testsrc):
    with tempfile.TemporaryDirectory(prefix='ecobot-audit-') as td:
        exe = os.path.join(td,'t.exe')
        comp = subprocess.run(['mcs','-langversion:7.2','-warnaserror','-out:'+exe]+[str(ROOT/s) for s in sources]+[str(ROOT/testsrc)],capture_output=True,text=True)
        if comp.returncode: return False, (comp.stdout+comp.stderr).strip()[:200]
        rn = subprocess.run(['mono',exe,td],capture_output=True,text=True)
        last = rn.stdout.strip().splitlines()[-1] if rn.stdout.strip() else rn.stderr.strip()[:200]
        return rn.returncode == 0 and 'passed, 0 failed' in rn.stdout, last
l1_ok, l1_msg = run_suite(['unity/Assets/Level1/Core/Puzzle.cs'],'tools/level1-tests/Tests.cs')
l2_ok, l2_msg = run_suite(['unity/Assets/Level1/Core/Puzzle.cs','unity/Assets/Level1/Core/Levels.cs'],'tools/level2-tests/Tests.cs')
check('logic suites re-run fresh against current shared model (L1 + L2)', harness_unchanged and l1_ok and l2_ok,
      'L1 harness == ' + baseline[:7] + '; L1: ' + l1_msg + ' | L2: ' + l2_msg)
report = {'scope':'Static source/configuration audit only; not Unity Editor compilation or gameplay verification',
          'checks':checks,'passed':sum(c['pass'] for c in checks),'failed':sum(not c['pass'] for c in checks),
          'unity_editor_verified':False,'android_verified':False,'upm_resolution_verified':False,
          'reviewed_api_sources':['https://github.com/Unity-Technologies/Graphics/blob/2022.3/staging/Packages/com.unity.render-pipelines.core/Runtime/Utilities/ResourceReloader.cs',
          'https://github.com/Unity-Technologies/Graphics/blob/2022.3/staging/Packages/com.unity.render-pipelines.universal/Runtime/Data/UniversalRenderPipelineAsset.cs'],
          'source_sha256':{str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(active + [scene,UNITY/'Packages/manifest.json'])}}
(ROOT/'docs/level1/STATIC_CHECKS.json').write_text(json.dumps(report,indent=2)+'\n')
sys.exit(0 if report['failed'] == 0 else 1)
