Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "nsDialogs.nsh"
!include "WinMessages.nsh"
!include "FileFunc.nsh"

!ifndef SOURCE_DIR
  !error "Use scripts/package.ps1 to supply SOURCE_DIR."
!endif
!ifndef OUTPUT_FILE
  !error "Use scripts/package.ps1 to supply OUTPUT_FILE."
!endif
!ifndef PRODUCT_VERSION
  !error "PRODUCT_VERSION is required."
!endif
!ifndef PRODUCT_FILE_VERSION
  !error "PRODUCT_FILE_VERSION is required."
!endif
!ifndef PRODUCT_LABEL
  !error "PRODUCT_LABEL is required."
!endif
!define MUI_ICON "..\src\AudioOutputManager.App\Assets\AuriHelm.ico"
!define MUI_UNICON "..\src\AudioOutputManager.App\Assets\AuriHelm.ico"
Name "${PRODUCT_LABEL}"
OutFile "${OUTPUT_FILE}"
RequestExecutionLevel user
InstallDir "$LOCALAPPDATA\Programs\AudioOutputManager"
SetCompressor /SOLID lzma
VIProductVersion "${PRODUCT_FILE_VERSION}"
VIAddVersionKey /LANG=1028 "ProductName" "${PRODUCT_LABEL}"
VIAddVersionKey /LANG=1028 "FileDescription" "AuriHelm 目前使用者安裝程式"
VIAddVersionKey /LANG=1028 "FileVersion" "${PRODUCT_VERSION}"
VIAddVersionKey /LANG=1028 "LegalCopyright" "Copyright (c) 2026 Kerry (kkldream)"

Var Expected
Var Stage
Var Previous
Var HadPrevious
Var RemoveDataCheckbox
Var RemoveDataState
Var ShortcutStage
Var HadShortcut
Var ShortcutChanged
Var HadRegistration
Var RegistrationKey
Var RollbackFailed

; Preserve the exact values that this installer owns, including absent values.
!macro RegistrationValue NAME KIND
  Var Old${NAME}
  Var Had${NAME}
!macroend
!insertmacro RegistrationValue DisplayName Str
!insertmacro RegistrationValue DisplayIcon Str
!insertmacro RegistrationValue DisplayVersion Str
!insertmacro RegistrationValue InstallLocation Str
!insertmacro RegistrationValue UninstallString Str
!insertmacro RegistrationValue NoModify DWORD
!insertmacro RegistrationValue NoRepair DWORD

!macro ReadRegistrationValue NAME KIND
  StrCpy $Had${NAME} 0
  ${If} $HadRegistration == 1
    System::Call 'advapi32::RegQueryValueExW(p $RegistrationKey, w "${NAME}", p 0, *i .r2, p 0, p 0) i .r0'
    ${If} $0 == 2
      ; The value was absent; rollback must keep it absent.
    ${ElseIf} $0 != 0
      Goto backupregistrationfailed
    ${Else}
      !if "${KIND}" == "Str"
        ${If} $2 != 1
      !else
        ${If} $2 != 4
      !endif
        ; Do not overwrite a value we cannot restore with its original type.
        Goto backupregistrationfailed
      ${EndIf}
      ClearErrors
      ReadReg${KIND} $Old${NAME} HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AudioOutputManager" "${NAME}"
      IfErrors backupregistrationfailed
      StrCpy $Had${NAME} 1
    ${EndIf}
  ${EndIf}
!macroend

!macro RestoreRegistrationValue NAME KIND
  ClearErrors
  ${If} $Had${NAME} == 1
    WriteReg${KIND} HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AudioOutputManager" "${NAME}" $Old${NAME}
  ${Else}
    ; Deleting an absent value reports an error; verify absence separately.
    DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AudioOutputManager" "${NAME}"
    ClearErrors
    ReadReg${KIND} $0 HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AudioOutputManager" "${NAME}"
    ${IfNot} ${Errors}
      StrCpy $RollbackFailed 1
    ${EndIf}
    ClearErrors
  ${EndIf}
  ${If} ${Errors}
    StrCpy $RollbackFailed 1
  ${EndIf}
!macroend

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\AudioOutputManager.App.exe"
!define MUI_FINISHPAGE_RUN_TEXT "$(LaunchNow)"
!define MUI_FINISHPAGE_RUN_FUNCTION LaunchApplication
!define MUI_FINISHPAGE_SHOWREADME
!define MUI_FINISHPAGE_SHOWREADME_TEXT "$(CreateDesktop)"
!define MUI_FINISHPAGE_SHOWREADME_NOTCHECKED
!define MUI_FINISHPAGE_SHOWREADME_FUNCTION CreateDesktopShortcut
!insertmacro MUI_PAGE_FINISH
UninstPage custom un.DataOptions un.DataOptionsLeave
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH
!insertmacro MUI_LANGUAGE "TradChinese"
!insertmacro MUI_LANGUAGE "English"

LangString LaunchNow ${LANG_TRADCHINESE} "立即開啟 AuriHelm"
LangString LaunchNow ${LANG_ENGLISH} "Open AuriHelm now"
LangString CreateDesktop ${LANG_TRADCHINESE} "建立桌面捷徑"
LangString CreateDesktop ${LANG_ENGLISH} "Create a desktop shortcut"
LangString LaunchFailed ${LANG_TRADCHINESE} "無法開啟 AuriHelm，請從開始功能表重試。"
LangString LaunchFailed ${LANG_ENGLISH} "AuriHelm could not be opened. Please retry from the Start menu."
LangString DesktopShortcutFailed ${LANG_TRADCHINESE} "未建立桌面捷徑：同名檔案已存在，或無法寫入桌面。既有檔案已保留。"
LangString DesktopShortcutFailed ${LANG_ENGLISH} "No desktop shortcut was created: a file already exists or the desktop is not writable. Existing files were preserved."

Function LaunchApplication
  IfSilent launchdone
  ClearErrors
  Exec '"$INSTDIR\AudioOutputManager.App.exe"'
  ${If} ${Errors}
    MessageBox MB_ICONEXCLAMATION "$(LaunchFailed)" /SD IDOK
  ${EndIf}
  launchdone:
FunctionEnd

Function CreateDesktopShortcut
  IfSilent desktopdone
  ; The reference is private to this installation. CopyFile's fail-if-exists
  ; flag also protects a same-name file created after our existence check.
  System::Call 'kernel32::GetFileAttributesW(w "$DESKTOP\AuriHelm.lnk") i .r0'
  ${If} $0 != -1
    Goto desktopfailed
  ${EndIf}
  SetOutPath "$INSTDIR"
  ClearErrors
  CreateShortcut "$INSTDIR\desktop-shortcut.lnk" "$INSTDIR\AudioOutputManager.App.exe" "" "$INSTDIR\Assets\AuriHelm.ico"
  IfErrors desktopfailed
  System::Call 'kernel32::CopyFileW(w "$INSTDIR\desktop-shortcut.lnk", w "$DESKTOP\AuriHelm.lnk", i 1) i .r0'
  ${If} $0 == 0
    Delete "$INSTDIR\desktop-shortcut.lnk"
    Goto desktopfailed
  ${EndIf}
  Goto desktopdone
  desktopfailed:
    MessageBox MB_ICONEXCLAMATION "$(DesktopShortcutFailed)" /SD IDOK
  desktopdone:
FunctionEnd

Function un.RemoveOwnedDesktopShortcut
  ; Compare the exact original bytes, not just the target path. An edited or
  ; replaced shortcut belongs to the user, even if it still starts this app.
  Push $0
  Push $1
  Push $2
  Push $3
  Push $4
  Push $5
  Push $6
  Push $7
  StrCpy $7 0
  System::Call 'kernel32::GetFileAttributesW(w "$INSTDIR\desktop-shortcut.lnk") i .r0'
  ${If} $0 == -1
    Goto desktopcheckdone
  ${EndIf}
  IntOp $0 $0 & 0x410
  ${If} $0 != 0
    Goto desktopcheckdone
  ${EndIf}
  System::Call 'kernel32::GetFileAttributesW(w "$DESKTOP\AuriHelm.lnk") i .r0'
  ${If} $0 == -1
    Goto desktopcheckdone
  ${EndIf}
  IntOp $0 $0 & 0x410
  ${If} $0 != 0
    Goto desktopcheckdone
  ${EndIf}
  ClearErrors
  FileOpen $0 "$INSTDIR\desktop-shortcut.lnk" r
  IfErrors desktopcheckdone
  FileOpen $1 "$DESKTOP\AuriHelm.lnk" r
  IfErrors desktopclosefirst
  FileSeek $0 0 END $2
  FileSeek $1 0 END $3
  IfErrors desktopcloseboth
  ${If} $2 != $3
  ${OrIf} $2 == 0
  ${OrIf} $2 > 65536
    Goto desktopcloseboth
  ${EndIf}
  FileSeek $0 0 SET
  FileSeek $1 0 SET
  IfErrors desktopcloseboth
  StrCpy $4 0
  desktopcompare:
    FileReadByte $0 $5
    IfErrors desktopcloseboth
    FileReadByte $1 $6
    IfErrors desktopcloseboth
    ${If} $5 != $6
      Goto desktopcloseboth
    ${EndIf}
    IntOp $4 $4 + 1
    ${If} $4 < $2
      Goto desktopcompare
    ${EndIf}
    StrCpy $7 1
  desktopcloseboth:
    FileClose $1
  desktopclosefirst:
    FileClose $0
  ${If} $7 == 1
    Delete "$DESKTOP\AuriHelm.lnk"
  ${EndIf}
  desktopcheckdone:
  Pop $7
  Pop $6
  Pop $5
  Pop $4
  Pop $3
  Pop $2
  Pop $1
  Pop $0
FunctionEnd

!macro VerifyPaths PREFIX
Function ${PREFIX}VerifyPaths
  SetShellVarContext current
  ; NSIS GetFullPathName clears its output when the destination does not exist.
  ; Use the Win32 lexical conversion, which does not require an installed app.
  StrCpy $Expected "$LOCALAPPDATA\Programs\AudioOutputManager"
  System::Call 'kernel32::GetFullPathNameW(w "$Expected", i ${NSIS_MAX_STRLEN}, w .r0, p 0) i .r1'
  ${If} $1 == 0
  ${OrIf} $1 >= ${NSIS_MAX_STRLEN}
  ${OrIf} $0 == ""
  ${OrIf} $0 != $Expected
    Goto invalidpath
  ${EndIf}
  ${If} $INSTDIR != $Expected
    Goto invalidpath
  ${EndIf}
  StrCpy $Stage "$Expected.new"
  StrCpy $Previous "$Expected.previous"
  ; Refuse directory links before recursive cleanup or a directory swap.
  Push $Expected
  Push $Stage
  Push $Previous
  Push "$LOCALAPPDATA\AudioOutputManager"
  Push "$LOCALAPPDATA\AudioOutputManager\logs"
  StrCpy $2 5
  nextpath:
    Pop $0
  checkparent:
    System::Call 'kernel32::GetFileAttributesW(w r0) i .r1'
    ${If} $1 != -1
      IntOp $1 $1 & 0x400
      ${If} $1 != 0
        Goto invalidpath
      ${EndIf}
    ${EndIf}
    ${GetParent} "$0" $3
    ${If} $3 != ""
    ${AndIf} $3 != $0
      StrCpy $0 $3
      Goto checkparent
    ${EndIf}
    IntOp $2 $2 - 1
    ${If} $2 > 0
      Goto nextpath
    ${EndIf}
  Return
  invalidpath:
    SetErrorLevel 1
    MessageBox MB_ICONSTOP "安裝路徑無效或經過目錄連結，已停止。 / Invalid installation path." /SD IDOK
    Abort
FunctionEnd
!macroend
!insertmacro VerifyPaths ""
!insertmacro VerifyPaths "un."

Function .onInit
  ${IfNot} ${RunningX64}
    SetErrorLevel 1
    MessageBox MB_ICONSTOP "需要 Windows 11 x64。 / Windows 11 x64 required." /SD IDOK
    Abort
  ${EndIf}
  ReadRegStr $0 HKLM "SOFTWARE\Microsoft\Windows NT\CurrentVersion" "CurrentBuildNumber"
  ${If} $0 < 22000
    SetErrorLevel 1
    MessageBox MB_ICONSTOP "需要 Windows 11（build 22000 以上）。" /SD IDOK
    Abort
  ${EndIf}
  Call VerifyPaths
FunctionEnd

!macro CloseExisting PREFIX
Function ${PREFIX}CloseExisting
  UserInfo::GetName
  Pop $0
  System::Call 'kernel32::OpenMutexW(i 0x00100000, i 0, w "Local\AudioOutputManager.$0") p.r1'
  ${If} $1 != 0
    System::Call 'kernel32::CloseHandle(p r1)'
    IfFileExists "$INSTDIR\AudioOutputManager.App.exe" 0 cannotclose
    ExecWait '"$INSTDIR\AudioOutputManager.App.exe" --request-exit' $2
    StrCpy $3 0
    waitexit:
      System::Call 'kernel32::OpenMutexW(i 0x00100000, i 0, w "Local\AudioOutputManager.$0") p.r1'
      ${If} $1 != 0
        System::Call 'kernel32::CloseHandle(p r1)'
        IntOp $3 $3 + 1
        ${If} $3 > 30
          Goto cannotclose
        ${EndIf}
        Sleep 1000
        Goto waitexit
      ${EndIf}
  ${EndIf}
  Return
  cannotclose:
    SetErrorLevel 1
    MessageBox MB_ICONSTOP "程式尚未正常結束，已停止；請先處理未套用的編輯。不會強制終止。 / Close the app before continuing." /SD IDOK
    Abort
FunctionEnd
!macroend
!insertmacro CloseExisting ""
!insertmacro CloseExisting "un."

Function BackupRegistration
  StrCpy $HadRegistration 0
  System::Call 'advapi32::RegOpenKeyExW(p 0x80000001, w "Software\Microsoft\Windows\CurrentVersion\Uninstall\AudioOutputManager", i 0, i 0x20019, *p .r1) i .r0'
  ${If} $0 == 0
    StrCpy $HadRegistration 1
    StrCpy $RegistrationKey $1
  ${ElseIf} $0 != 2
    ; Permission or other lookup failures are not equivalent to a missing key.
    SetErrors
    Return
  ${EndIf}
  !insertmacro ReadRegistrationValue DisplayName Str
  !insertmacro ReadRegistrationValue DisplayIcon Str
  !insertmacro ReadRegistrationValue DisplayVersion Str
  !insertmacro ReadRegistrationValue InstallLocation Str
  !insertmacro ReadRegistrationValue UninstallString Str
  !insertmacro ReadRegistrationValue NoModify DWORD
  !insertmacro ReadRegistrationValue NoRepair DWORD
  ${If} $HadRegistration == 1
    System::Call 'advapi32::RegCloseKey(p $RegistrationKey)'
  ${EndIf}
  ClearErrors
  Return
  backupregistrationfailed:
    System::Call 'advapi32::RegCloseKey(p $RegistrationKey)'
    SetErrors
FunctionEnd

Function RestoreRegistration
  !insertmacro RestoreRegistrationValue DisplayName Str
  !insertmacro RestoreRegistrationValue DisplayIcon Str
  !insertmacro RestoreRegistrationValue DisplayVersion Str
  !insertmacro RestoreRegistrationValue InstallLocation Str
  !insertmacro RestoreRegistrationValue UninstallString Str
  !insertmacro RestoreRegistrationValue NoModify DWORD
  !insertmacro RestoreRegistrationValue NoRepair DWORD
  ${If} $HadRegistration == 0
    ClearErrors
    DeleteRegKey /ifempty HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AudioOutputManager"
    ${If} ${Errors}
      StrCpy $RollbackFailed 1
    ${EndIf}
  ${EndIf}
FunctionEnd

Section "Install"
  Call VerifyPaths
  ; These paths are fixed and validated before any recursive operation.
  SetOutPath "$TEMP"
  ClearErrors
  IfFileExists "$Stage\." 0 createstage
  RMDir /r "$Stage"
  IfErrors badstage
  createstage:
  ClearErrors
  CreateDirectory "$Stage"
  IfErrors badstage
  SetOutPath "$Stage"
  IfErrors badstage
  ClearErrors
  File /r "${SOURCE_DIR}\*.*"
  IfErrors badstage
  WriteUninstaller "$Stage\Uninstall.exe"
  IfErrors badstage
  Call CloseExisting
  InitPluginsDir
  Call BackupRegistration
  IfErrors badstage
  StrCpy $ShortcutStage "$SMPROGRAMS\AuriHelm.lnk.new"
  StrCpy $HadShortcut 0
  StrCpy $ShortcutChanged 0
  System::Call 'kernel32::GetFileAttributesW(w "$ShortcutStage") i .r0'
  ${If} $0 != -1
    IntOp $0 $0 & 0x410
    ${If} $0 != 0
      Goto badstage
    ${EndIf}
  ${EndIf}
  IfFileExists "$SMPROGRAMS\AuriHelm.lnk" 0 backupcomplete
    System::Call 'kernel32::GetFileAttributesW(w "$SMPROGRAMS\AuriHelm.lnk") i .r0'
    IntOp $0 $0 & 0x410
    ${If} $0 != 0
      Goto badstage
    ${EndIf}
    ClearErrors
    CopyFiles /SILENT "$SMPROGRAMS\AuriHelm.lnk" "$PLUGINSDIR\AuriHelm.lnk"
    IfErrors badstage
    StrCpy $HadShortcut 1
  backupcomplete:
  ; Settings remain outside the program directory. Never migrate them during install.
  IfFileExists "$LOCALAPPDATA\AudioOutputManager\settings.json" 0 nobackup
    ClearErrors
    CopyFiles /SILENT "$LOCALAPPDATA\AudioOutputManager\settings.json" "$LOCALAPPDATA\AudioOutputManager\settings.pre-upgrade.json"
    IfErrors badstage
  nobackup:
  SetOutPath "$TEMP"
  StrCpy $HadPrevious 0
  StrCpy $RollbackFailed 0
  IfFileExists "$INSTDIR\." 0 swapnew
    ; Do not replace an unrelated or incomplete directory silently.
    IfFileExists "$INSTDIR\AudioOutputManager.App.exe" 0 badstage
    ; Keep desktop ownership across upgrades, including a silent upgrade.
    IfFileExists "$INSTDIR\desktop-shortcut.lnk" 0 desktopreferencecopied
      ClearErrors
      CopyFiles /SILENT "$INSTDIR\desktop-shortcut.lnk" "$Stage\desktop-shortcut.lnk"
      IfErrors badstage
    desktopreferencecopied:
    ClearErrors
    IfFileExists "$Previous\." 0 moveold
    RMDir /r "$Previous"
    IfErrors badstage
    moveold:
    ClearErrors
    Rename "$INSTDIR" "$Previous"
    IfErrors badstage
    StrCpy $HadPrevious 1
  swapnew:
    ClearErrors
    Rename "$Stage" "$INSTDIR"
    IfErrors restoreold
    ClearErrors
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AudioOutputManager" "DisplayName" "${PRODUCT_LABEL}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AudioOutputManager" "DisplayIcon" '$\"$INSTDIR\Assets\AuriHelm.ico$\"'
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AudioOutputManager" "DisplayVersion" "${PRODUCT_VERSION}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AudioOutputManager" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AudioOutputManager" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
    WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AudioOutputManager" "NoModify" 1
    WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AudioOutputManager" "NoRepair" 1
    IfErrors rollbacknew
    ; Prepare the link before replacing the original, so a locked original is
    ; left intact and the registry/files can still be rolled back.
    CreateShortcut "$ShortcutStage" "$INSTDIR\AudioOutputManager.App.exe" "" "$INSTDIR\Assets\AuriHelm.ico"
    IfErrors rollbacknew
    ${If} $HadShortcut == 1
      Delete "$SMPROGRAMS\AuriHelm.lnk"
      IfErrors rollbacknew
    ${EndIf}
    StrCpy $ShortcutChanged 1
    Rename "$ShortcutStage" "$SMPROGRAMS\AuriHelm.lnk"
    IfErrors rollbacknew
    Delete "$SMPROGRAMS\Audio Output Manager.lnk"
    ; Optional launch/desktop actions belong to the interactive finish page.
    ; No startup entry, service, task or background update is installed here.
    Goto done
  rollbacknew:
    StrCpy $RollbackFailed 0
    Call RestoreRegistration
    ${If} $ShortcutChanged == 1
      ClearErrors
      ${If} $HadShortcut == 1
        CopyFiles /SILENT "$PLUGINSDIR\AuriHelm.lnk" "$SMPROGRAMS\AuriHelm.lnk"
      ${Else}
        Delete "$SMPROGRAMS\AuriHelm.lnk"
      ${EndIf}
      ${If} ${Errors}
        StrCpy $RollbackFailed 1
      ${EndIf}
    ${EndIf}
    Delete "$ShortcutStage"
    ClearErrors
    Rename "$INSTDIR" "$Stage"
    ${If} ${Errors}
      StrCpy $RollbackFailed 1
    ${EndIf}
  restoreold:
    ${If} $HadPrevious == 1
      ClearErrors
      Rename "$Previous" "$INSTDIR"
      ${If} ${Errors}
        StrCpy $RollbackFailed 1
      ${EndIf}
    ${EndIf}
    SetErrorLevel 1
    ${If} $RollbackFailed == 1
      SetErrorLevel 2
    ${EndIf}
    MessageBox MB_ICONSTOP "安裝未完成；已嘗試還原原狀。請保留 .previous／.new 目錄供復原。 / Installation failed; recovery files were retained." /SD IDOK
    Abort
  badstage:
    SetErrorLevel 1
    MessageBox MB_ICONSTOP "無法完成安裝準備，原程式與設定未移轉。 / Staging failed." /SD IDOK
    Abort
  done:
SectionEnd

Function un.onInit
  Call un.VerifyPaths
  StrCpy $RemoveDataState ${BST_UNCHECKED}
FunctionEnd
Function un.DataOptions
  nsDialogs::Create 1018
  Pop $0
  ${If} $0 == error
    Abort
  ${EndIf}
  ${NSD_CreateLabel} 0 0 100% 36u "解除安裝預設保留設定與日誌。自行匯出的檔案不會刪除。 / Settings and logs are retained by default; exported files are never removed."
  Pop $0
  ${NSD_CreateCheckbox} 0 42u 100% 24u "一併刪除本工具設定與日誌 / Also delete app settings and logs"
  Pop $RemoveDataCheckbox
  ${NSD_SetState} $RemoveDataCheckbox $RemoveDataState
  nsDialogs::Show
FunctionEnd
Function un.DataOptionsLeave
  ${NSD_GetState} $RemoveDataCheckbox $RemoveDataState
FunctionEnd
Section "Uninstall"
  Call un.VerifyPaths
  Call un.CloseExisting
  SetOutPath "$TEMP"
  Call un.RemoveOwnedDesktopShortcut
  Delete "$SMPROGRAMS\Audio Output Manager.lnk"
  Delete "$SMPROGRAMS\AuriHelm.lnk"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "AudioOutputManager"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AudioOutputManager"
  RMDir /r "$INSTDIR"
  RMDir /r "$Previous"
  RMDir /r "$Stage"
  ${If} $RemoveDataState == ${BST_CHECKED}
    StrCpy $0 "$LOCALAPPDATA\AudioOutputManager"
    System::Call 'kernel32::GetFullPathNameW(w r0, i ${NSIS_MAX_STRLEN}, w .r1, p 0) i .r2'
    ${If} $2 > 0
    ${AndIf} $2 < ${NSIS_MAX_STRLEN}
    ${AndIf} $0 == $1
      ; Only remove files owned by this app, preserving manually exported files.
      Delete "$0\settings.json"
      Delete "$0\settings.json.bak"
      Delete "$0\settings.pre-upgrade.json"
      Delete "$0\settings.json.corrupt-*"
      Delete "$0\logs\events.log"
      Delete "$0\logs\events.1.log"
      Delete "$0\logs\events.2.log"
      Delete "$0\logs\events.3.log"
      Delete "$0\logs\events.4.log"
      RMDir "$0\logs"
      RMDir "$0"
    ${EndIf}
  ${EndIf}
SectionEnd
