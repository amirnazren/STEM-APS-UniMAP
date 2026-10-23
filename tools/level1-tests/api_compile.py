"""Optional secondary API/syntax check, NOT Unity 2022.3 Editor compilation.
Uses older third-party redistributed reference assemblies from NuGet in a TEMP folder.
No binary is shipped or referenced by the Unity project. Real Editor validation is still required.
"""
from pathlib import Path
import io, subprocess, tempfile, urllib.request, zipfile, json
ROOT=Path(__file__).resolve().parents[2]
packages=[('unityengine.modules','2021.3.33'),('unity3d.unityengine.ui','2018.3.5.1')]
with tempfile.TemporaryDirectory(prefix='ecobot-api-') as temp:
    folder=Path(temp)
    for package,version in packages:
        url=f'https://api.nuget.org/v3-flatcontainer/{package}/{version}/{package}.{version}.nupkg'
        with urllib.request.urlopen(url,timeout=90) as response: archive=zipfile.ZipFile(io.BytesIO(response.read()))
        for name in archive.namelist():
            if name.endswith('.dll') and ('net45/' in name or package.endswith('.ui')):
                (folder/Path(name).name).write_bytes(archive.read(name))
    names=['UnityEngine','UnityEngine.CoreModule','UnityEngine.UI','UnityEngine.AudioModule','UnityEngine.TextRenderingModule','UnityEngine.UIModule','UnityEngine.InputLegacyModule','UnityEngine.IMGUIModule']
    command=['mcs','-langversion:7.2','-warnaserror','-target:library','-out:'+str(folder/'ApiCheck.dll')]
    command+=['-r:'+str(folder/(name+'.dll')) for name in names]
    command+=[str(ROOT/'unity/Assets/Level1/Core/Puzzle.cs')]
    command+=map(str, sorted((ROOT/'unity/Assets/Level1/Runtime').glob('*.cs')))
    result=subprocess.run(command,capture_output=True,text=True)
    print(result.stdout+result.stderr)
    if result.returncode: raise SystemExit(result.returncode)
    report={'check':'Secondary source/API compilation only','result':'passed','errors':0,'warnings':0,'compiler':'Mono mcs 6.8.0.105','references':dict(packages),'files':'Puzzle.cs + all Level1/Runtime/*.cs','unity_2022_editor_compilation_verified':False,'editor_helpers_compiled':False,'caveat':'Older reference DLLs, NOT a Unity build. Editor imports, scene deserialization, package resolution and interaction remain unverified.'}
    (ROOT/'docs/level1/API_COMPILE.json').write_text(json.dumps(report,indent=2)+'\n')
    print('Reference API compilation passed with warnings-as-errors. This is NOT Unity Editor compilation.')
