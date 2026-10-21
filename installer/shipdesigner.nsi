Unicode true
!include "MUI2.nsh"
Name "Судно-Проект"
OutFile "..\dist\ShipDesigner-Setup-1.0.0.exe"
InstallDir "$LOCALAPPDATA\Programs\ShipDesigner"
RequestExecutionLevel user
SetCompressor /SOLID lzma
!define MUI_ICON "..\src\ShipDesigner\app.ico"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\ShipDesigner.exe"
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Russian"
Section "ShipDesigner"
  SetOutPath "$INSTDIR"
  File /r "..\out\ShipDesigner\*.*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\Судно-Проект"
  CreateShortcut "$SMPROGRAMS\Судно-Проект\Судно-Проект.lnk" "$INSTDIR\ShipDesigner.exe"
  CreateShortcut "$SMPROGRAMS\Судно-Проект\Удалить.lnk" "$INSTDIR\Uninstall.exe"
  CreateShortcut "$DESKTOP\Судно-Проект.lnk" "$INSTDIR\ShipDesigner.exe"
  WriteRegStr HKCU "Software\Classes\.shipproj" "" "ShipDesigner.Project"
  WriteRegStr HKCU "Software\Classes\ShipDesigner.Project\shell\open\command" "" '"$INSTDIR\ShipDesigner.exe" "%1"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ShipDesigner" "DisplayName" "Судно-Проект: расчёт судов и чертежи"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ShipDesigner" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ShipDesigner" "DisplayIcon" "$INSTDIR\ShipDesigner.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ShipDesigner" "DisplayVersion" "1.0.0"
SectionEnd
Section "Uninstall"
  ExecWait 'taskkill /f /im ShipDesigner.exe'
  RMDir /r "$INSTDIR"
  RMDir /r "$SMPROGRAMS\Судно-Проект"
  Delete "$DESKTOP\Судно-Проект.lnk"
  DeleteRegKey HKCU "Software\Classes\ShipDesigner.Project"
  DeleteRegKey HKCU "Software\Classes\.shipproj"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ShipDesigner"
SectionEnd
