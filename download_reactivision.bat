@echo off
echo Downloading reacTIVision...
powershell -Command "& {[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://sourceforge.net/projects/reactivision/files/reacTIVision/1.5.1/reacTIVision-1.5.1-win64.zip/download' -OutFile 'reacTIVision.zip'}"
echo Done - extracted to reacTIVision folder
pause