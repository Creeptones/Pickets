procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
  Started: Boolean;
begin
  { Run before the mutex check and before any files or shortcuts are removed.
    A running Pickets saves its layout and hands recovery to this process. }
  if CurUninstallStep <> usAppMutexCheck then Exit;
  repeat
    Started := Exec(ExpandConstant('{app}\{#MyAppExeName}'),
      '--restore-icons-silent', ExpandConstant('{app}'), SW_HIDE,
      ewWaitUntilTerminated, ResultCode);
    if Started and (ResultCode = 0) then Exit;
    Log(Format('Icon recovery failed (started=%d, code=%d). Uninstall is blocked.', [Ord(Started), ResultCode]));
    if UninstallSilent then Abort;
    if MsgBox('Pickets could not confirm that all desktop icons were restored.' + #13#10#13#10 +
      'No application files have been removed. Make sure Windows Explorer is running ' +
      'and close any open Pickets setup guide, then choose Retry.' + #13#10#13#10 +
      'If this continues, choose Cancel. Your app and saved layouts will remain available for recovery.',
      mbError, MB_RETRYCANCEL) <> IDRETRY then Abort;
  until False;
end;
