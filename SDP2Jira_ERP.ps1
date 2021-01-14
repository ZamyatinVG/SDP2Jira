param 
(
    $json = "none"
)
$jsondata = Get-Content $json -Encoding UTF8
$obj = ConvertFrom-Json $jsondata
$RequestID = $obj.request.id
$LoginName = $obj.LOGIN_NAME

$result = cmd /c "C:\Soft\SDP2Jira\SDP2Jira.exe -r $RequestID -u $LoginName -proj ERP"
$result
#Start-Sleep -Milliseconds 10000