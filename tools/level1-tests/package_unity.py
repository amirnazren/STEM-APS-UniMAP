"""Package tracked Unity source only; exclude caches, secrets, binaries and the platform identity.
Usage: python3 tools/level1-tests/package_unity.py /absolute/output.zip
No dependency installs, Editor execution or scene regeneration occur.
"""
from pathlib import Path, PurePosixPath
import hashlib
import json
import subprocess
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[2]
output = Path(sys.argv[1]).resolve()
if output.suffix.lower() != '.zip': raise SystemExit('Output must end in .zip')
files = subprocess.check_output(['git','ls-files','-z','unity/Assets','unity/Packages','unity/ProjectSettings','unity/README_LEVEL1.md','docs/level1'],cwd=ROOT).decode().split('\0')
files = sorted(p for p in files if p and PurePosixPath(p).name not in {'.keep', '.gitkeep'})
for name in files:
    path = PurePosixPath(name)
    assert not any(part.startswith('.') or part.lower() in {'library','temp','node_modules','obj','logs','build','builds','usersettings','__pycache__'} for part in path.parts), name
    assert path.suffix.lower() not in {'.dll','.exe','.apk','.aab','.zip','.log','.pyc'}, name
    assert not (ROOT/name).is_symlink(), name
required={'unity/Assets/Scenes/MainGameScene.unity','unity/Assets/Scenes/MainGameScene.unity.meta','unity/Packages/manifest.json','unity/ProjectSettings/ProjectVersion.txt','unity/ProjectSettings/EditorBuildSettings.asset','unity/README_LEVEL1.md'}
assert required <= set(files)
commit = subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()
for name in files:
    tracked = subprocess.check_output(['git','show',commit+':'+name],cwd=ROOT)
    assert tracked == (ROOT/name).read_bytes(), 'Uncommitted packaged file: '+name
output.parent.mkdir(parents=True,exist_ok=True)
folder='EcoBotBuilders-Level1'
manifest={'source_repository':'https://github.com/amirnazren/STEM-APS-UniMAP','commit':commit,
          'open_folder':folder+'/unity','scene':'Assets/Scenes/MainGameScene.unity',
          'unity_editor_version':'2022.3.21f1','runtime_verified':False,
          'sha256':{name:hashlib.sha256((ROOT/name).read_bytes()).hexdigest() for name in files}}
with zipfile.ZipFile(output,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as archive:
    for name in files: archive.write(ROOT/name,folder+'/'+name)
    archive.writestr(folder+'/SOURCE_MANIFEST.json',json.dumps(manifest,indent=2)+'\n')
    archive.writestr(folder+'/START_HERE.txt','Eco-Bot Builders - offline Level 1\n\nExtract all files. In Unity Hub add the unity folder using Unity 2022.3.21f1.\nAllow the initial Unity package download. Open Assets/Scenes/MainGameScene.unity and press Play.\nNo backend, room PIN or SocketIOUnity is needed. Hint 3 can load the example program.\n\nRead unity/README_LEVEL1.md for controls and validation evidence.\nSource-level checks passed; Unity Editor interaction and Android build remain unverified.\nGit commit: '+commit+'\n')
with zipfile.ZipFile(output) as archive:
    assert archive.testzip() is None
    assert len(archive.namelist()) == len(files)+2
    for name in files:
        assert hashlib.sha256(archive.read(folder+'/'+name)).hexdigest() == manifest['sha256'][name]
print(json.dumps({'zip':str(output),'source_commit':commit,'source_files':len(files),'zip_entries':len(files)+2,'bytes':output.stat().st_size,'sha256':hashlib.sha256(output.read_bytes()).hexdigest(),'crc_and_source_hash_verification':'passed'},indent=2))
