"""Linux-only: actual AppImage helper replacement/relaunch and rollback.

All targets are temporary fixtures below mods/_qa; never updates the live app.
"""
import json,os,shutil,subprocess,sys,tempfile,time,hashlib
from pathlib import Path

root=Path(__file__).resolve().parents[1]
image=Path(sys.argv[1]).resolve()
tool=root/'dist/appimagetool-1.9.1'
env=os.environ.copy();env['APPIMAGE_EXTRACT_AND_RUN']='1';env['ARCH']='x86_64'
with tempfile.TemporaryDirectory(prefix='appimage-update-',dir=root/'mods/_qa') as temporary:
    fixture=Path(temporary)
    probe=fixture/'ProbeDir';probe.mkdir()
    (probe/'AppRun').write_text('#!/bin/sh\nprintf ok > "$(dirname "$APPIMAGE")/probe-launched"\n',encoding='utf-8')
    (probe/'AppRun').chmod(0o755)
    (probe/'probe.desktop').write_text('[Desktop Entry]\nType=Application\nName=Updater probe\nExec=AppRun\nIcon=probe\nCategories=Utility;\n',encoding='utf-8')
    shutil.copyfile(root/'gestor/Assets/icon.png',probe/'probe.png')
    payload=fixture/'probe.AppImage'
    subprocess.run([str(tool),'--appimage-extract-and-run','--no-appstream',str(probe),str(payload)],env=env,check=True,stdout=subprocess.DEVNULL)
    for invalid in (False,True):
        home=fixture/('failure' if invalid else 'success');home.mkdir()
        target=home/'manager.AppImage';target.write_text('original executable');target.chmod(0o755)
        (home/'keep-data').write_text('unchanged')
        stage=home/'.SanabiGestor-update-fixture';stage.mkdir()
        shutil.copyfile(image,stage/'helper.AppImage');(stage/'helper.AppImage').chmod(0o755)
        if invalid:
            header=bytearray(128);header[:4]=b'\x7fELF';header[8:10]=b'AI';header[10]=2
            (stage/'payload').write_bytes(header)
        else:shutil.copyfile(payload,stage/'payload')
        sha=hashlib.sha256((stage/'payload').read_bytes()).hexdigest()
        job=dict(target=str(target),sha256=sha,parentId=2147483647,parentStarted=0,version='99.0.0')
        (stage/'job.json').write_text(json.dumps(job))
        result=subprocess.run([str(stage/'helper.AppImage'),'--apply-update',str(stage/'job.json')],env=env,timeout=40)
        status=(stage/'result.txt').read_text()
        if invalid:
            assert result.returncode==1 and status.startswith('ERROR') and target.read_text()=='original executable',status
        else:
            assert result.returncode==0 and status=='OK 99.0.0',status
            assert (stage/'previous').read_text()=='original executable'
            for _ in range(100):
                if (home/'probe-launched').exists():break
                time.sleep(.05)
            assert (home/'probe-launched').read_text()=='ok','New AppImage did not restart'
        assert (home/'keep-data').read_text()=='unchanged'
    print('PASS: Linux AppImage actual helper replacement/relaunch, invalid-executable rollback and portable data preservation.')
