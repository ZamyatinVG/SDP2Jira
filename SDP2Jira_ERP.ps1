param 
(
    $json = "none"
)
$jsondata = Get-Content $json -Encoding UTF8
$obj = ConvertFrom-Json $jsondata
$RequestID = $obj.request.id

$result = cmd /c "C:\Soft\SDP2Jira\SDP2Jira.exe -r $RequestID -proj ERP"
$result
#Start-Sleep -Milliseconds 10000