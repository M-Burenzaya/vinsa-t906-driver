c:
cd C:\Windows\System32
regsvr32 /s inked.dll
cd C:\Program Files\Common Files\Microsoft Shared\ink
regsvr32 /s inkdiv.dll
regsvr32 /s inkobj.dll
regsvr32 /s tpcps.dll

if not EXIST "%SystemDrive%\Program Files (x86)\Common Files\microsoft shared\ink\inkdiv.dll" GOTO exit
cd C:\Windows\SysWOW64
regsvr32 /s inked.dll
cd C:\Program Files (x86)\Common Files\microsoft shared\ink
regsvr32 /s inkdiv.dll
regsvr32 /s inkobj.dll
regsvr32 /s tpcps.dll

:exit