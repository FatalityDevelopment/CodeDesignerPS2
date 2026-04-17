

git clone --recurse-submodules https://github.com/TylerDev888/CodeDesigner.git

Invoke-WebRequest "https://github.com/PCSX2/pcsx2-windows-dependencies/releases/download/latest-windows-dependencies/pcsx2-windows-dependencies.7z" -OutFile deps.7z; New-Item -ItemType Directory -Force pcsx2 | Out-Null; & "C:\Program Files\7-Zip\7z.exe" x deps.7z -o"pcsx2" -y; Remove-Item deps.7z