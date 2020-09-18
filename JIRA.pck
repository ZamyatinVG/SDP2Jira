create or replace package JIRA is

  -- Author  : ZAMYATINVG
  -- Created : 23.07.2020 11:25:12
  -- Purpose : 
  
  type Issue_ROW is record
  (
     fday varchar2(20),
     specialist varchar2(255),
     status varchar2(40),
     category varchar2(255),
     direction varchar2(255),
     open_count number,
     open_point number,
     closed_count number,
     closed_point number,
     all_count number,
     all_point number
  );
  type Issue_TBL is table of Issue_ROW;
  
  function IssueStats return Issue_TBL pipelined;

end JIRA;
/
create or replace package body JIRA is

  function IssueStats return Issue_TBL pipelined is
  begin
    for i in (select to_char(t.fday, '''YYYY.MM.DD') fday, 
                     t.assigneeuser, t.statusname, t.category, t.direction,
                     sum(t.open_count) open_count,  
                     sum(t.open_point) open_point,
                     sum(t.closed_count) closed_count,
                     sum(t.closed_point) closed_point,  
                     sum(t.all_count) all_count,
                     sum(t.all_point) all_point        
                from
                (
                  select t.*,
                         case when t.statusname <> '10. �������' then t.point else 0 end open_point,
                         case when t.statusname <> '10. �������' then t.count else 0 end open_count,
                         case when t.statusname = '10. �������' then t.point else 0 end closed_point,
                         case when t.statusname = '10. �������' then t.count else 0 end closed_count,
                         t.point all_point,
                         t.count all_count,
                         row_number() over (partition by t.jiraidentifier, t.statusname order by t.fday) r2
                    from
                    (
                      select ji.jiraidentifier,
                             calendar.fday,
                             case
                               when ji.assigneeuser in ('������� �������� �����������',
                                                        '������� ���� �������',
                                                        '����������� ������ ����������',
                                                        '����������� ����� ����������',
                                                        '������������ ������� ������������',
                                                        '������� ����� �������������',
                                                        '������� ������ ��������')
                               then 'Dev: ' || ji.assigneeuser
                               else 'Sup: ' || ji.assigneeuser
                             end assigneeuser,
                             case 
                               when jih.tovalue is null and trunc(ji.created) <= trunc(calendar.fday) then '01. �����'
                               when jih.tovalue in ('�����', '������') then '01. �����'
                               when jih.tovalue in ('To Do') then '02. �������'
                               when jih.tovalue in ('� ������') then '03. � ������'
                               when jih.tovalue in ('TODAY') then '04. �� �������'
                               when jih.tovalue in ('�������� ���������') then '05. �������� ���������'
                               when jih.tovalue in ('�������� ��') then '06. �������� ��'
                               when jih.tovalue in ('������', '������������') then '07. ������������'
                               when jih.tovalue in ('����� ��') then '08. ����� ��'
                               when jih.tovalue in ('����� ���������') then '09. ����� ���������'
                               when jih.tovalue in ('���������', '��������', 'Done', '�������') then '10. �������'
                               else ' '
                             end statusname,
                             nvl(ji.category, ' ') category,
                             nvl(ji.direction, ' ') direction,
                             1 count,
                             case when ji.rate = 0 then 5 else ji.rate end point,
                             row_number() over (partition by calendar.fday, ji.jiraidentifier order by jih.createddate desc) r1
                        from
                        (select to_date('17.06.2020', 'DD.MM.YYYY') - 1 + rownum fday
                           from all_objects
                           where rownum < 1000
                        ) calendar
                        left join gal_asup.jira_issue ji on trunc(calendar.fday) >= trunc(ji.created) 
                        left join gal_asup.jira_issue_history jih on trunc(calendar.fday) >= trunc(jih.createddate) 
                                                                  and jih.jiraidentifier = ji.jiraidentifier
                        where calendar.fday < sysdate
                        and to_char(calendar.fday, 'D') not in ('7', '1')
                    ) t
                    where t.r1 = 1
                ) t
                where (t.statusname <> '10. �������' or (t.statusname = '10. �������' and t.r2 <= 6))
                group by t.fday, t.assigneeuser, t.statusname, t.category, t.direction
             )
    loop
      pipe row(i);
    end loop;
  end IssueStats;
  
end JIRA;
/
