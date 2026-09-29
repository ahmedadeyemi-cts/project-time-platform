import { useEffect, useRef, useState } from 'react';
import { flowHiveErrorText, optionalGuid } from './flowhive-plan-request.js';
import './project-flowhive-collaboration.css';

const emptyContact={displayName:'',email:'',phone:'',title:'',organization:'',contactKind:'customer',isActive:true};
export default function ProjectFlowHiveCollaboration({projectId,data,error,canManage,onRefresh,postJson,getJson,meetingsOnly=false}) {
  const [contact,setContact]=useState(null),[meetingOpen,setMeetingOpen]=useState(false);
  const [meeting,setMeeting]=useState({title:'',agenda:'',location:'',startsAt:'',endsAt:'',attendeeReferences:[]});
  const [busy,setBusy]=useState(''),[notice,setNotice]=useState(''),[failure,setFailure]=useState('');
  const alive=useRef(true),pending=useRef(false);
  useEffect(()=>{alive.current=true;return()=>{alive.current=false;};},[projectId]);
  const loaded=data?.projectId===projectId,ready=loaded && data.ready;
  const allowed=ready && canManage && data.canManage;
  const team=loaded?data.team || []:[], contacts=loaded?data.contacts || []:[];
  const active=contacts.filter(p=>p.isActive),drafts=loaded?data.meetingDrafts || []:[];
  const timezone=Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
  const attendees=[...team.filter(p=>p.email).map(p=>({...p,key:`user:${p.userId}`,kind:'Team'})),...active.map(p=>({...p,key:`contact:${p.projectContactId}`,kind:'External'}))];
  async function run(action,work) {
    if(pending.current || !allowed)return;
    pending.current=true;setBusy(action);setFailure('');setNotice('');
    try {await work();if(alive.current)await onRefresh(projectId);}
    catch(e){if(alive.current)setFailure(flowHiveErrorText(e));}
    finally{pending.current=false;if(alive.current)setBusy('');}
  }
  async function saveContact(e){e.preventDefault();await run('contact',async()=>{
    const result=await postJson(`/api/project-flowhive/projects/${projectId}/contacts`,{...contact,projectContactId:optionalGuid(contact.projectContactId,'Contact'),expectedRowVersion:optionalGuid(contact.expectedRowVersion,'Contact version')});
    if(result.projectId!==projectId || !optionalGuid(result.projectContactId,'Saved contact') || !optionalGuid(result.rowVersion,'Saved contact version')) throw new Error('The saved contact could not be verified. Refresh the project contacts before retrying.');
    if(alive.current){setContact(null);setNotice('Project contact saved. No account, access permission or invitation was created.');}
  });}
  async function archiveContact(person){if(!window.confirm(`Archive ${person.displayName} from this project? Existing history will be retained.`))return;
    await run('contact',async()=>{await postJson(`/api/project-flowhive/projects/${projectId}/contacts`,{...person,expectedRowVersion:person.rowVersion,isActive:false});if(alive.current)setNotice('Contact archived.');});}
  async function saveMeeting(e){e.preventDefault();await run('meeting',async()=>{
    if(!meeting.startsAt || !meeting.endsAt)throw new Error('Select a start and finish for the meeting.');
    const startsAt=new Date(meeting.startsAt),endsAt=new Date(meeting.endsAt);
    if(!Number.isFinite(startsAt.getTime()) || !Number.isFinite(endsAt.getTime()) || endsAt<=startsAt)throw new Error('Meeting finish must be after its start.');
    const result=await postJson(`/api/project-flowhive/projects/${projectId}/meeting-drafts`,{...meeting,startsAt:startsAt.toISOString(),endsAt:endsAt.toISOString(),timezoneName:timezone});
    if(result.invitationSent!==false || result.projectId!==projectId)throw new Error('The meeting draft result could not be verified. Refresh before retrying.');
    if(alive.current){setMeetingOpen(false);setMeeting({title:'',agenda:'',location:'',startsAt:'',endsAt:'',attendeeReferences:[]});setNotice('Meeting draft saved. Invitations have not been sent. Review the calendar draft in Outlook or your calendar application.');}
  });}
  async function download(draft){await run('calendar',async()=>{
    const response=await getJson(`/api/project-flowhive/projects/${projectId}/meeting-drafts/${draft.meetingDraftId}/calendar`);
    if(!(response instanceof Response))throw new Error('Calendar draft could not be downloaded.');
    const blob=await response.blob();if(!alive.current)return;
    const url=URL.createObjectURL(blob),anchor=document.createElement('a');anchor.href=url;anchor.download='project-meeting-draft.ics';anchor.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
    setNotice('Calendar draft downloaded. Pulse has not sent an invitation or created a Teams meeting.');
  });}
  return <section className="flowhive-collaboration-card" aria-label={meetingsOnly?'Project meeting planning':'Project team and customer contacts'}>
    <header><div><span className="flowhive-home-eyebrow">People & collaboration</span><h3>{meetingsOnly?'Plan a project meeting':'Project team & customer contacts'}</h3><p>Know who is involved. Keep customer contacts separate from internal user accounts.</p></div>
      <div className="flowhive-collaboration-actions">{!meetingsOnly && <button type="button" disabled={!allowed || Boolean(busy) || active.length>=15} onClick={()=>{setContact({...emptyContact});setFailure('');}}>Create customer info</button>}
      <button type="button" disabled={!allowed || Boolean(busy)} onClick={()=>setMeetingOpen(value=>!value)}>Plan meeting</button></div></header>
    {failure && <p role="alert" className="flowhive-collaboration-error">{failure}</p>}{notice && <p role="status">{notice}</p>}
    {!loaded && <p role="status">{error || 'Loading project people…'} <button type="button" onClick={()=>onRefresh(projectId)}>Refresh team</button></p>}
    {loaded && !ready && <p role="status">The project team is available. Contact editing and meeting drafts are waiting for the project-collaboration database update.</p>}
    {!meetingsOnly && <>
      <h4>Internal project team <span>{team.length}</span></h4>
      <div className="flowhive-team-grid">{team.map(p=><article key={p.userId}><span className="flowhive-team-initials" aria-hidden="true">{p.displayName.split(' ').slice(0,2).map(v=>v[0]).join('')}</span><div><strong>{p.displayName}</strong><span>{p.role}</span>{p.title && <small>{p.title}</small>}<small>{p.email}</small>{p.phone && <small>{p.phone}</small>}</div></article>)}</div>
      {loaded && !team.length && <p>No internal project team is recorded. Assign the PM and delivery team through the existing project workflow.</p>}
      <h4>Customer, vendor & partner contacts <span>{active.length}/15 active</span></h4>
      <p className="flowhive-collaboration-help">These are people linked to this project, not new customer organizations. Assigning them work does not grant Pulse access or send email/Teams messages.</p>
      <div className="flowhive-contact-list">{contacts.map(p=><article key={p.projectContactId}><div><strong>{p.displayName}</strong><span>{p.title}{p.organization?` · ${p.organization}`:''}</span><small>{p.email}{p.phone?` · ${p.phone}`:''}</small><small>{p.contactKind} · {p.isActive?'Active contact':'Archived'}</small></div>
        {allowed && p.isActive && <div><button type="button" disabled={Boolean(busy)} onClick={()=>setContact({...p,expectedRowVersion:p.rowVersion})}>Edit</button><button type="button" disabled={Boolean(busy)} onClick={()=>archiveContact(p)}>Archive</button></div>}</article>)}</div>
      {ready && !contacts.length && <p>No project customer contacts yet. Create one contact at a time, up to 15 active contacts.</p>}
    </>}
    {contact && allowed && <form className="flowhive-contact-form" onSubmit={saveContact}><h4>{contact.projectContactId?'Edit project contact':'Create customer info'}</h4><div className="flowhive-collaboration-fields">
      <label>Name<input required minLength="2" maxLength="200" value={contact.displayName} onChange={e=>setContact({...contact,displayName:e.target.value})}/></label>
      <label>Email<input required type="email" maxLength="320" value={contact.email} onChange={e=>setContact({...contact,email:e.target.value})}/></label>
      <label>Phone number<input type="tel" maxLength="80" value={contact.phone} onChange={e=>setContact({...contact,phone:e.target.value})}/></label>
      <label>Title<input maxLength="160" value={contact.title} onChange={e=>setContact({...contact,title:e.target.value})}/></label>
      <label>Organization<input maxLength="200" value={contact.organization} onChange={e=>setContact({...contact,organization:e.target.value})}/></label>
      <label>Contact type<select value={contact.contactKind} onChange={e=>setContact({...contact,contactKind:e.target.value})}><option value="customer">Customer</option><option value="vendor">Vendor</option><option value="partner">Partner</option></select></label>
      </div><button type="submit" disabled={Boolean(busy)}>{busy==='contact'?'Saving contact…':'Save customer info'}</button><button type="button" disabled={Boolean(busy)} onClick={()=>setContact(null)}>Cancel</button></form>}
    {meetingOpen && allowed && <form className="flowhive-meeting-draft-form" onSubmit={saveMeeting}><h4>Meeting draft</h4><p>Times are entered in {timezone}. External attendees’ availability is unknown; confirm it with them. This saves a draft, not a sent invitation.</p><div className="flowhive-collaboration-fields">
      <label>Meeting title<input required minLength="2" maxLength="240" value={meeting.title} onChange={e=>setMeeting({...meeting,title:e.target.value})}/></label>
      <label>Location or reviewed meeting URL<input maxLength="300" value={meeting.location} onChange={e=>setMeeting({...meeting,location:e.target.value})}/></label>
      <label>Starts<input type="datetime-local" required value={meeting.startsAt} onChange={e=>setMeeting({...meeting,startsAt:e.target.value})}/></label>
      <label>Ends<input type="datetime-local" required min={meeting.startsAt || undefined} value={meeting.endsAt} onChange={e=>setMeeting({...meeting,endsAt:e.target.value})}/></label>
      </div><label>Customer-visible agenda<textarea maxLength="8000" value={meeting.agenda} onChange={e=>setMeeting({...meeting,agenda:e.target.value})}/></label>
      <fieldset><legend>Select attendees from this project</legend>{attendees.map(p=><label className="flowhive-person-option" key={p.key}><input type="checkbox" checked={meeting.attendeeReferences.includes(p.key)} onChange={e=>setMeeting({...meeting,attendeeReferences:e.target.checked?[...meeting.attendeeReferences,p.key]:meeting.attendeeReferences.filter(key=>key!==p.key)})}/><span>{p.displayName}<small>{p.kind} · {p.email}</small></span></label>)}</fieldset>
      <button type="submit" disabled={Boolean(busy) || !meeting.attendeeReferences.length}>{busy==='meeting'?'Saving meeting draft…':'Save meeting draft'}</button><button type="button" disabled={Boolean(busy)} onClick={()=>setMeetingOpen(false)}>Cancel</button></form>}
    <details className="flowhive-meeting-draft-history"><summary>Meeting drafts <span>{drafts.length} · not sent</span></summary>{drafts.map(draft=><article key={draft.meetingDraftId}><div><strong>{draft.title}</strong><span>{new Date(draft.startsAt).toLocaleString()} – {new Date(draft.endsAt).toLocaleTimeString()} · {draft.timezoneName}</span><small>{draft.attendeeReferences.length} selected attendees · invitations not sent</small></div>{allowed && <button type="button" disabled={Boolean(busy)} onClick={()=>download(draft)}>Download calendar draft</button>}</article>)}{!drafts.length && <p>No meeting drafts have been saved.</p>}</details>
  </section>;
}
