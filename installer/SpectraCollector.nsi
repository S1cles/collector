Unicode true
!ifndef VERSION
  !error "VERSION is not defined: pass /DVERSION=<x.y.z>"
!endif
!ifndef OUTFILE
  !define OUTFILE "SpectraCollector-Setup.exe"
!endif
!ifndef APPEXE
  !define APPEXE "SpectraCollector.exe"
!endif
!ifndef ICON
  !define ICON "SpectraCollector.ico"
!endif

!include "MUI2.nsh"

!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\SpectraCollector"

Name "SpectraCollector"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\SpectraCollector"
RequestExecutionLevel user
SetCompressor /SOLID lzma

VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "SpectraCollector"
VIAddVersionKey "CompanyName" "Lyncis"
VIAddVersionKey "LegalCopyright" "Lyncis"
VIAddVersionKey "FileDescription" "SpectraCollector installer"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"

!define MUI_ICON "${ICON}"
!define MUI_UNICON "${ICON}"
!define MUI_FINISHPAGE_RUN "$INSTDIR\SpectraCollector.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Run SpectraCollector"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Section "SpectraCollector" SecApp
  SectionIn RO
  SetShellVarContext current
  SetOutPath "$INSTDIR"
  File "${APPEXE}"
  CreateShortcut "$SMPROGRAMS\SpectraCollector.lnk" "$INSTDIR\SpectraCollector.exe"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "SpectraCollector"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\SpectraCollector.exe"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "Lyncis"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
  SectionGetSize ${SecApp} $0
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "EstimatedSize" $0
SectionEnd

Section "Desktop shortcut" SecDesktop
  SetShellVarContext current
  CreateShortcut "$DESKTOP\SpectraCollector.lnk" "$INSTDIR\SpectraCollector.exe"
SectionEnd

Section "Uninstall"
  SetShellVarContext current
  Delete "$SMPROGRAMS\SpectraCollector.lnk"
  Delete "$DESKTOP\SpectraCollector.lnk"
  Delete "$INSTDIR\SpectraCollector.exe"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  DeleteRegKey HKCU "${UNINSTALL_KEY}"
SectionEnd
