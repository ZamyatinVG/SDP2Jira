USE [master]
GO
/****** Object:  Database [Jira]    Script Date: 22.06.2021 8:50:03 ******/
CREATE DATABASE [Jira] ON  PRIMARY 
( NAME = N'Jira', FILENAME = N'E:\MSSQL.BI\DATA\Jira.mdf' , SIZE = 1048576KB , MAXSIZE = UNLIMITED, FILEGROWTH = 1024KB )
 LOG ON 
( NAME = N'Jira_log', FILENAME = N'E:\MSSQL.BI\DATA\Jira_log.ldf' , SIZE = 158656KB , MAXSIZE = 2048GB , FILEGROWTH = 10%)
GO
ALTER DATABASE [Jira] SET COMPATIBILITY_LEVEL = 100
GO
IF (1 = FULLTEXTSERVICEPROPERTY('IsFullTextInstalled'))
begin
EXEC [Jira].[dbo].[sp_fulltext_database] @action = 'enable'
end
GO
ALTER DATABASE [Jira] SET ANSI_NULL_DEFAULT OFF 
GO
ALTER DATABASE [Jira] SET ANSI_NULLS OFF 
GO
ALTER DATABASE [Jira] SET ANSI_PADDING OFF 
GO
ALTER DATABASE [Jira] SET ANSI_WARNINGS OFF 
GO
ALTER DATABASE [Jira] SET ARITHABORT OFF 
GO
ALTER DATABASE [Jira] SET AUTO_CLOSE OFF 
GO
ALTER DATABASE [Jira] SET AUTO_SHRINK OFF 
GO
ALTER DATABASE [Jira] SET AUTO_UPDATE_STATISTICS ON 
GO
ALTER DATABASE [Jira] SET CURSOR_CLOSE_ON_COMMIT OFF 
GO
ALTER DATABASE [Jira] SET CURSOR_DEFAULT  GLOBAL 
GO
ALTER DATABASE [Jira] SET CONCAT_NULL_YIELDS_NULL OFF 
GO
ALTER DATABASE [Jira] SET NUMERIC_ROUNDABORT OFF 
GO
ALTER DATABASE [Jira] SET QUOTED_IDENTIFIER OFF 
GO
ALTER DATABASE [Jira] SET RECURSIVE_TRIGGERS OFF 
GO
ALTER DATABASE [Jira] SET  DISABLE_BROKER 
GO
ALTER DATABASE [Jira] SET AUTO_UPDATE_STATISTICS_ASYNC OFF 
GO
ALTER DATABASE [Jira] SET DATE_CORRELATION_OPTIMIZATION OFF 
GO
ALTER DATABASE [Jira] SET TRUSTWORTHY OFF 
GO
ALTER DATABASE [Jira] SET ALLOW_SNAPSHOT_ISOLATION OFF 
GO
ALTER DATABASE [Jira] SET PARAMETERIZATION SIMPLE 
GO
ALTER DATABASE [Jira] SET READ_COMMITTED_SNAPSHOT OFF 
GO
ALTER DATABASE [Jira] SET HONOR_BROKER_PRIORITY OFF 
GO
ALTER DATABASE [Jira] SET RECOVERY SIMPLE 
GO
ALTER DATABASE [Jira] SET  MULTI_USER 
GO
ALTER DATABASE [Jira] SET PAGE_VERIFY CHECKSUM  
GO
ALTER DATABASE [Jira] SET DB_CHAINING OFF 
GO
EXEC sys.sp_db_vardecimal_storage_format N'Jira', N'ON'
GO
USE [Jira]
GO
/****** Object:  User [NT SERVICE\HealthService]    Script Date: 22.06.2021 8:50:03 ******/
CREATE USER [NT SERVICE\HealthService] FOR LOGIN [NT SERVICE\HealthService]
GO
/****** Object:  User [jirabot]    Script Date: 22.06.2021 8:50:03 ******/
CREATE USER [jirabot] FOR LOGIN [jirabot] WITH DEFAULT_SCHEMA=[dbo]
GO
sys.sp_addrolemember @rolename = N'db_owner', @membername = N'jirabot'
GO
/****** Object:  Table [dbo].[ISSUE]    Script Date: 22.06.2021 8:50:03 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ISSUE](
	[JIRAIDENTIFIER] [nvarchar](255) NOT NULL,
	[KEY] [nvarchar](255) NULL,
	[PRIORITY] [nvarchar](255) NULL,
	[CREATED] [datetime] NULL,
	[REPORTERUSER] [nvarchar](255) NULL,
	[ASSIGNEEUSER] [nvarchar](255) NULL,
	[SUMMARY] [nvarchar](255) NULL,
	[STATUSNAME] [nvarchar](255) NULL,
	[STORYPOINTS] [decimal](10, 2) NULL,
	[CATEGORY] [nvarchar](255) NULL,
	[DIRECTION] [nvarchar](255) NULL,
	[UPDATED] [datetime] NULL,
	[TYPE] [nvarchar](255) NULL,
 CONSTRAINT [PK_JIRA_ISSUE] PRIMARY KEY CLUSTERED 
(
	[JIRAIDENTIFIER] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[ISSUE_HISTORY]    Script Date: 22.06.2021 8:50:03 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ISSUE_HISTORY](
	[ID] [nvarchar](255) NOT NULL,
	[JIRAIDENTIFIER] [nvarchar](255) NULL,
	[CREATEDDATE] [datetime] NULL,
	[FIELDNAME] [nvarchar](255) NULL,
	[FROMVALUE] [nvarchar](255) NULL,
	[TOVALUE] [nvarchar](255) NULL
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[LOG]    Script Date: 22.06.2021 8:50:03 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[LOG](
	[ID] [int] IDENTITY(1,1) NOT NULL,
	[LOGDATE] [datetime] NULL,
	[LOGLEVEL] [nvarchar](255) NULL,
	[USERNAME] [nvarchar](255) NULL,
	[HOSTNAME] [nvarchar](255) NULL,
	[HOSTIP] [nvarchar](20) NULL,
	[MESSAGE] [nvarchar](4000) NULL,
	[VERSION] [nvarchar](20) NULL,
 CONSTRAINT [PK_JIRA_LOG] PRIMARY KEY CLUSTERED 
(
	[ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[SERVICEDESK_WO]    Script Date: 22.06.2021 8:50:03 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[SERVICEDESK_WO](
	[fday] [datetime] NULL,
	[spec] [nvarchar](255) NULL,
	[opencount] [int] NULL
) ON [PRIMARY]
GO
/****** Object:  View [dbo].[ERP_STATS]    Script Date: 22.06.2021 8:50:03 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO


CREATE view [dbo].[ERP_STATS] as
with gen as 
	 (
		select 0 AS num
		union all
		select num + 1 from gen where num < 1000
		
	 )
select '''' + convert(nvarchar, t.fday) fday, 
       t.assigneeuser, t.statusname, t.category, t.direction,
       sum(t.open_count) open_count,  
       sum(t.open_point) open_point,
       sum(t.closed_count) closed_count,
       sum(t.closed_point) closed_point,  
       sum(t.all_count) all_count,
       sum(t.all_point) all_point
	from
	(
		select  t.*,
				case when t.statusname <> '07. Закрыта' then t.point else 0 end open_point,
				case when t.statusname <> '07. Закрыта' then t.count else 0 end open_count,
				case when t.statusname = '07. Закрыта' then t.point else 0 end closed_point,
				case when t.statusname = '07. Закрыта' then t.count else 0 end closed_count,
				t.point all_point,
				t.count all_count,
				row_number() over (partition by t.jiraidentifier, t.statusname order by t.fday) r2
		from
		(
			select ji.jiraidentifier,
				   calendar.fday,
				   case
					 when ji.assigneeuser in ('Замятин Вячеслав Геннадьевич',
											  'Гаменюк Аким Юрьевич',
											  'Горячевский Виктор Николаевич',
											  'Карашманова Ирина Михайловна',
											  'Никоноренков Алексей Валентинович',
											  'Торопов Роман Александрович',
											  'Фомичев Андрей Олегович')
					 then 'Dev: ' + ji.assigneeuser
					 when ji.assigneeuser in ('Зинина Марина Александровна',
											  'Горшкова Наталия Алексеевна',
											  'Шарапова Галина Адольфовна',
											  'Балунова Наталья Анатольевна',
											  'Конев Сергей Евгеньевич',
											  'Коноплев Илья Андреевич',
											  'Должиков Максим Станиславович',
											  'Арсеньева Ирина Викторовна',
											  'Горбунова Наталья Вячеславовна',
											  'Сутягин Александр Сергеевич',
											  'Курганова Нина Олеговна [X]',
											  'Каленых Юрий Николаевич',
											  'Купаев Сергей Сергеевич [X]')
                     then 'Sup: ' + ji.assigneeuser
					 else ji.assigneeuser
				   end assigneeuser,
				   case 
					 when jih.tovalue is null and convert(date, ji.created) <= convert(date, calendar.fday) then '01. Новая'
					 when jih.tovalue in ('Новая', 'Бэклог', 'Backlog') then '01. Новая'
					 when jih.tovalue in ('To Do', 'Сделать', 'Отложено', 'Запланировано') then '02. Сделать'
					 when jih.tovalue in ('В работе', 'TODAY', 'Сегодня', 'In Progress', 'В разработке') then '03. В работе'
					 when jih.tovalue in ('Ожидание Заказчика', 'Риски заказчика', 'Согласование с БП') then '04. Ожидание Заказчика'
					 when jih.tovalue in ('Ожидание ИТ', 'Риски ИТ') then '05. Ожидание ИТ'
					 when jih.tovalue in ('Приёмка', 'Тестирование', 'На проверке') then '06. Тестирование'
					 when jih.tovalue in ('Выполнена', 'Отменена', 'Done', 'Закрыта', 'Готово', 'Cancelled', 'Ждёт релиза') then '07. Закрыта'
					 else '00. Прочее'
				   end statusname,
				   isnull(ji.category, ' ') category,
				   isnull(ji.direction, ' ') direction,
				   1 count,
				   case when ji.storypoints = 0 then 5 else ji.storypoints end point,
				   row_number() over (partition by calendar.fday, ji.jiraidentifier order by jih.createddate desc) r1
			  from
			  (
				  select dateadd(day, num, convert(date, '2020-06-17')) fday
					from gen
			  ) calendar
			  left join dbo.issue ji on convert(date, calendar.fday) >= convert(date, ji.created) 
									 and (ji."KEY" like 'ERP-%'
									   or ji.assigneeuser in ('Замятин Вячеслав Геннадьевич',
															  'Гаменюк Аким Юрьевич',
															  'Горячевский Виктор Николаевич',
															  'Карашманова Ирина Михайловна',
															  'Никоноренков Алексей Валентинович',
															  'Торопов Роман Александрович',
															  'Фомичев Андрей Олегович',
															  'Зинина Марина Александровна',
															  'Горшкова Наталия Алексеевна',
															  'Шарапова Галина Адольфовна',
															  'Балунова Наталья Анатольевна',
															  'Конев Сергей Евгеньевич',
															  'Коноплев Илья Андреевич',
															  'Должиков Максим Станиславович',
															  'Арсеньева Ирина Викторовна',
															  'Горбунова Наталья Вячеславовна',
															  'Сутягин Александр Сергеевич',
															  'Курганова Нина Олеговна [X]',
															  'Каленых Юрий Николаевич',
															  'Купаев Сергей Сергеевич [X]'))
			  left join dbo.issue_history jih on convert(date, calendar.fday) >= convert(date, jih.createddate) 
	    									  and jih.jiraidentifier = ji.jiraidentifier
			  
			  where calendar.fday < getdate()
		) t
		where t.r1 = 1
	) t
	where (t.statusname <> '07. Закрыта' or (t.statusname = '07. Закрыта' and t.r2 <= 7))
	and datepart(weekday, t.fday) not in ('7', '1')
	group by t.fday, t.assigneeuser, t.statusname, t.category, t.direction
	--option (maxrecursion 10000)

GO
/****** Object:  View [dbo].[ISSUE_STATS]    Script Date: 22.06.2021 8:50:03 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE view [dbo].[ISSUE_STATS] as
with gen as 
	 (
		select 0 AS num
		union all
		select num + 1 from gen where num < 1000
		
	 )
select '''' + convert(nvarchar, t.fday) fday, 
       t.assigneeuser, t.statusname, t.project,
       sum(t.open_count) open_count,  
       sum(t.closed_count) closed_count,  
       sum(t.all_count) all_count
	from
	(
		select  t.*,
				case when t.statusname <> '07. Закрыта' then t.count else 0 end open_count,
				case when t.statusname = '07. Закрыта' then t.count else 0 end closed_count,
				t.count all_count,
				row_number() over (partition by t.jiraidentifier, t.statusname order by t.fday) r2
		from
		(
			select ji.jiraidentifier,
				   calendar.fday,
				   ji.assigneeuser,
				   SUBSTRING("KEY", 1, CHARINDEX('-', "KEY", 1) - 1) project,
				   case 
					 when jih.tovalue is null and convert(date, ji.created) <= convert(date, calendar.fday) then '01. Новая'
					 when jih.tovalue in ('Новая', 'Бэклог', 'Backlog') then '01. Новая'
					 when jih.tovalue in ('To Do', 'Сделать', 'Отложено', 'Запланировано', 'Нужно сделать', 'Готово к разработке', 'Очередь на исполнение', 'Ждёт проработки') then '02. Сделать'
					 when jih.tovalue in ('В работе', 'TODAY', 'Сегодня', 'In Progress', 'В разработке', 'Анализ', 'Проработка') then '03. В работе'
					 when jih.tovalue in ('Ожидание Заказчика', 'Риски заказчика', 'Согласование с БП', 'На проверке бизнесом', 'Согласование', 'Ожидание', 'Ожидание ответа') then '04. Ожидание Заказчика'
					 when jih.tovalue in ('Ожидание ИТ', 'Риски ИТ') then '05. Ожидание ИТ'
					 when jih.tovalue in ('Приёмка', 'Тестирование', 'На проверке', 'Ревью') then '06. Тестирование'
					 when jih.tovalue in ('Выполнена', 'Отменена', 'Done', 'Закрыта', 'Готово', 'Cancelled', 'Ждёт релиза') then '07. Закрыта'
					 else '00. Прочее'
				   end statusname,
				   1 count,
				   row_number() over (partition by calendar.fday, ji.jiraidentifier order by jih.createddate desc) r1
			  from
			  (
				  select dateadd(day, num, convert(date, '2020-01-01')) fday
					from gen
			  ) calendar
			  left join dbo.issue ji on convert(date, calendar.fday) >= convert(date, ji.created) 
			  left join dbo.issue_history jih on convert(date, calendar.fday) >= convert(date, jih.createddate) 
	    									  and jih.jiraidentifier = ji.jiraidentifier
			  
			  where calendar.fday < getdate()
		) t
		where t.r1 = 1
	) t
	where (t.statusname <> '07. Закрыта' or (t.statusname = '07. Закрыта' and t.r2 <= 7))
	and datepart(weekday, t.fday) not in ('7', '1')
	group by t.fday, t.assigneeuser, t.statusname, t.project
	--option (maxrecursion 10000)

GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [SK_KEY]    Script Date: 22.06.2021 8:50:03 ******/
CREATE UNIQUE NONCLUSTERED INDEX [SK_KEY] ON [dbo].[ISSUE]
(
	[KEY] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [PK_ID_FIELDNAME]    Script Date: 22.06.2021 8:50:03 ******/
CREATE UNIQUE NONCLUSTERED INDEX [PK_ID_FIELDNAME] ON [dbo].[ISSUE_HISTORY]
(
	[ID] ASC,
	[FIELDNAME] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
ALTER TABLE [dbo].[ISSUE_HISTORY]  WITH CHECK ADD  CONSTRAINT [FK_ISSUE_HISTORY_ISSUE] FOREIGN KEY([JIRAIDENTIFIER])
REFERENCES [dbo].[ISSUE] ([JIRAIDENTIFIER])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[ISSUE_HISTORY] CHECK CONSTRAINT [FK_ISSUE_HISTORY_ISSUE]
GO
USE [master]
GO
ALTER DATABASE [Jira] SET  READ_WRITE 
GO
