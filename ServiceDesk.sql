USE [msdb]
GO

/****** Object:  Job [ServiceDesk]    Script Date: 16.10.2020 9:34:24 ******/
BEGIN TRANSACTION
DECLARE @ReturnCode INT
SELECT @ReturnCode = 0
/****** Object:  JobCategory [[Uncategorized (Local)]]    Script Date: 16.10.2020 9:34:24 ******/
IF NOT EXISTS (SELECT name FROM msdb.dbo.syscategories WHERE name=N'[Uncategorized (Local)]' AND category_class=1)
BEGIN
EXEC @ReturnCode = msdb.dbo.sp_add_category @class=N'JOB', @type=N'LOCAL', @name=N'[Uncategorized (Local)]'
IF (@@ERROR <> 0 OR @ReturnCode <> 0) GOTO QuitWithRollback

END

DECLARE @jobId BINARY(16)
EXEC @ReturnCode =  msdb.dbo.sp_add_job @job_name=N'ServiceDesk', 
		@enabled=1, 
		@notify_level_eventlog=0, 
		@notify_level_email=0, 
		@notify_level_netsend=0, 
		@notify_level_page=0, 
		@delete_level=0, 
		@description=N'No description available.', 
		@category_name=N'[Uncategorized (Local)]', 
		@owner_login_name=N'HCASKONA\ZamyatinVG', @job_id = @jobId OUTPUT
IF (@@ERROR <> 0 OR @ReturnCode <> 0) GOTO QuitWithRollback
/****** Object:  Step [WorkOrder]    Script Date: 16.10.2020 9:34:24 ******/
EXEC @ReturnCode = msdb.dbo.sp_add_jobstep @job_id=@jobId, @step_name=N'WorkOrder', 
		@step_id=1, 
		@cmdexec_success_code=0, 
		@on_success_action=1, 
		@on_success_step_id=0, 
		@on_fail_action=2, 
		@on_fail_step_id=0, 
		@retry_attempts=0, 
		@retry_interval=0, 
		@os_run_priority=0, @subsystem=N'TSQL', 
		@command=N'truncate table dbo.servicedesk_wo
go
insert into dbo.servicedesk_wo
select *
from OPENQUERY(SERVICEDESK,
''select d.day fday, 
       u.lastname || '''' '''' || u.firstname spec, 
       count(td.workorderid) opencount
  from sduser u
  cross join (select distinct from_unixtime(wo.createdtime / 1000)::date AS day FROM workorder wo
              union 
              select distinct from_unixtime(wo.createdtime / 1000)::date AS day FROM arc_workorder wo) d
  left join (select wo.workorderid,
		    date_trunc(''''day'''', from_unixtime(wo.createdtime / 1000) + ''''03:00:00''''::interval) startdate,
		    date_trunc(''''day'''', case when wo.completedtime in (0, -1) then null else from_unixtime(wo.completedtime / 1000) + ''''03:00:00''''::interval end) enddate,
		    case when qd.queuename = ''''outsourcing ERP'''' then 18307 else wos.ownerid end ownerid,
		    coalesce(wof.udf_double3, 0) points
	      from workorder wo
	      join workorderstates wos on wo.workorderid = wos.workorderid and wos.statusid <> 1802
	      join sduser u on wos.ownerid = u.userid
	      left join workorder_queue woq on wo.workorderid = woq.workorderid
	      left join queuedefinition qd on woq.queueid = qd.queueid
	      left join workorder_fields wof on wo.workorderid = wof.workorderid
	    ) td on u.userid = td.ownerid and d.day >= td.startdate and (d.day <= td.enddate or td.enddate is null)
  where u.lastname || '''' '''' || u.firstname in (''''Балунова Наталья'''', ''''Горбунова Наталья'''', ''''Горшкова Наталия Алексеевна'''', ''''Должиков Максим'''', ''''Конев Сергей'''', 
					     ''''Коноплев Илья'''', ''''Сутягин Александр'''', ''''Шарапова Галина'''', ''''Курганова Нина'''',
					     ''''Зинина Марина'''', ''''Купаев Сергей Сергеевич'''', ''''Арсеньева Ирина Викторовна'''')
  and d.day >= ''''2020-06-17''''
  group by d.day, u.lastname, u.firstname
'')
go', 
		@database_name=N'Jira', 
		@flags=0
IF (@@ERROR <> 0 OR @ReturnCode <> 0) GOTO QuitWithRollback
EXEC @ReturnCode = msdb.dbo.sp_update_job @job_id = @jobId, @start_step_id = 1
IF (@@ERROR <> 0 OR @ReturnCode <> 0) GOTO QuitWithRollback
EXEC @ReturnCode = msdb.dbo.sp_add_jobschedule @job_id=@jobId, @name=N'Daily', 
		@enabled=1, 
		@freq_type=4, 
		@freq_interval=1, 
		@freq_subday_type=1, 
		@freq_subday_interval=0, 
		@freq_relative_interval=0, 
		@freq_recurrence_factor=0, 
		@active_start_date=20201009, 
		@active_end_date=99991231, 
		@active_start_time=95000, 
		@active_end_time=235959, 
		@schedule_uid=N'c1a2011a-a983-4289-a81d-34cbf29bbb63'
IF (@@ERROR <> 0 OR @ReturnCode <> 0) GOTO QuitWithRollback
EXEC @ReturnCode = msdb.dbo.sp_add_jobserver @job_id = @jobId, @server_name = N'(local)'
IF (@@ERROR <> 0 OR @ReturnCode <> 0) GOTO QuitWithRollback
COMMIT TRANSACTION
GOTO EndSave
QuitWithRollback:
    IF (@@TRANCOUNT > 0) ROLLBACK TRANSACTION
EndSave:
GO


