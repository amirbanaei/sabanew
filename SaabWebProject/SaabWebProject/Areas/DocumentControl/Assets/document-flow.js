(function () {
'use strict';
const root = document.getElementById('df-app');
if (!root) return;
const $ = s => root.querySelector(s);
const base = root.dataset.base;
const titles = {dashboard:'داشبورد مدارک',types:'تعریف مدارک',workflow:'تنظیم مراحل تأیید',submit:'بارگذاری مدرک',tracking:'پیگیری مدارک',inbox:'کارتابل بررسی'};
const statuses = ['برگشت برای اصلاح','در حال بررسی','تأیید نهایی','تأیید مشروط'];
const decisions = ['عودت برای اصلاح','تأیید','تأیید مشروط'];
let state, requests = [], currentPage, workflowVersion = '', currentDetail, workflowLoad = 0;
const escape = v => String(v == null ? '' : v).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const number = n => Number(n).toLocaleString('fa-IR');
const money = value => { const parts=String(value).split('.'); return parts[0].replace(/\B(?=(\d{3})+(?!\d))/g,'٬').replace(/\d/g,d=>'۰۱۲۳۴۵۶۷۸۹'[Number(d)])+(parts[1]?'٫'+parts[1].replace(/\d/g,d=>'۰۱۲۳۴۵۶۷۸۹'[Number(d)]):''); };
const version = v => Array.isArray(v) ? btoa(String.fromCharCode.apply(null,v)) : v;
const badge = status => '<span class="badge status-'+status+'">'+statuses[status]+'</span>';
function date(value) {
    if (!value) return '—';
    const match = /\/Date\((-?\d+)/.exec(value);
    const d = new Date(match ? Number(match[1]) : value);
    return new Intl.DateTimeFormat('fa-IR',{dateStyle:'medium',timeStyle:'short',timeZone:'Asia/Tehran'}).format(d);
}
function notice(message,error) { const el=$('#message'); el.hidden=false; el.classList.toggle('error',!!error); el.textContent=message; }
async function api(action,data) {
    const options={credentials:'same-origin',headers:{'X-Requested-With':'XMLHttpRequest'}};
    if(data) { options.method='POST'; data.set('__RequestVerificationToken',$('#anti-forgery input').value); options.body=data; }
    const response=await fetch(base+action,options);
    if(response.status===401 || response.status===403) throw new Error('نشست شما پایان یافته یا دسترسی ندارید؛ دوباره وارد سامانه شوید.');
    let result;
    try { result=await response.json(); } catch (_) { throw new Error('پاسخ سرور معتبر نیست. ورود به سامانه و اجرای اسکریپت دیتابیس را بررسی کنید.'); }
    if(!response.ok || !result.success) throw new Error(result.message || 'عملیات انجام نشد.');
    return result.data;
}
async function attempt(work,button) {
    if(button) button.disabled=true;
    try { await work(); } catch(error) { notice(error.message,true); }
    finally { if(button) button.disabled=false; }
}
function options(items,placeholder) { return '<option value="">'+escape(placeholder||'انتخاب کنید')+'</option>'+items.map(x=>'<option value="'+x.Id+'">'+escape(x.Title)+(x.IsActive===false?' (غیرفعال)':'')+'</option>').join(''); }
function page(name) {
    if(!state) return;
    if(!state.manager && ['dashboard','types','workflow'].includes(name)) return;
    closeSelects();
    currentPage=name;
    root.querySelectorAll('[data-panel]').forEach(e=>e.hidden=e.dataset.panel!==name);
    root.querySelectorAll('.nav-item').forEach(e=>{ const active=e.dataset.page===name; e.classList.toggle('active',active);e.setAttribute('aria-selected',String(active));e.tabIndex=active?0:-1; });
    $('#page-title').textContent=titles[name];
}
function table(rows) {
    if(!rows.length) return '<div class="empty">هنوز مدرکی برای نمایش وجود ندارد.</div>';
    return '<table><thead><tr><th>مدرک / پیمان</th><th>دوره</th><th>وضعیت</th><th>مرحله جاری</th><th>آخرین تغییر</th><th></th></tr></thead><tbody>'+rows.map(r=>'<tr><td><strong>'+escape(r.TypeTitle)+'</strong><br><small>'+escape(r.ContractTitle)+'</small></td><td>'+number(r.PeriodYear)+' / '+number(r.PeriodMonth)+'</td><td>'+badge(r.Status)+'</td><td>'+escape(r.StepTitle||'—')+'</td><td>'+date(r.UpdatedUtc)+'</td><td><button class="secondary" data-detail="'+r.Id+'">'+(r.CanReview?'بررسی مدرک':'مشاهده')+'</button></td></tr>').join('')+'</tbody></table>';
}
function renderRequests() {
    const stats=[['کل مدارک',requests.length],['در حال بررسی',requests.filter(r=>r.Status===1).length],['نیازمند اصلاح',requests.filter(r=>r.Status===0).length],['تأیید نهایی',requests.filter(r=>r.Status===2).length],['تأیید مشروط',requests.filter(r=>r.Status===3).length]];
    $('#stats').innerHTML=stats.map(s=>'<div class="glass stat"><strong>'+number(s[1])+'</strong><span>'+s[0]+'</span></div>').join('');
    $('#recent').innerHTML=table(requests.slice(0,6));
    const term=$('#search').value.trim(); const status=$('#status-filter').value;
    $('#tracking-list').innerHTML=table(requests.filter(r=>(!term||(r.TypeTitle+' '+r.ContractTitle).includes(term)) && (status===''||r.Status===Number(status))));
    $('#inbox-list').innerHTML=table(requests.filter(r=>r.CanReview));
    $('#inbox-nav').hidden=!state.manager&&!requests.some(r=>r.OwnerId!==state.userId||r.CanReview);
}
function renderTypes() {
    $('#type-list').innerHTML=state.types.length?state.types.map(t=>'<div class="type-card"><div><strong>'+escape(t.Title)+'</strong> <span class="badge '+(!t.IsActive?'status-0':'')+'">'+(t.IsActive?'فعال':'غیرفعال')+'</span><small>'+(t.MonthOffset===-1?'ماه قبل':'ماه جاری')+' · '+escape(t.SampleName||'بدون فایل نمونه')+'</small></div><button class="secondary" data-edit-type="'+t.Id+'">ویرایش</button></div>').join(''):'<div class="empty">اولین نوع مدرک را تعریف کنید.</div>';
}
function updatePeriod() {
    if(!state) return;
    const t=state.types.find(t=>t.Id===Number($('#submit-type').value));
    let [year,month]=state.period;
    if(t&&t.MonthOffset===-1) { month--; if(month===0) {month=12;year--;} }
    $('#period-label').textContent=t?number(year)+' / '+number(month):'نوع مدرک را انتخاب کنید';
    const available=!!(t&&t.SampleName&&$('#contract-select').value);
    $('#sample-link').hidden=!available;
    $('#sample-empty').hidden=available;
    $('#sample-empty').textContent=t&&t.SampleName?'برای دانلود نمونه، پیمان را انتخاب کنید':'فایل نمونه تعریف نشده است';
    if(available) $('#sample-link').href=base+'Sample?id='+t.Id+'&contractId='+$('#contract-select').value;
}
async function refresh() {
    const oldType=$('#submit-type').value, oldContract=$('#contract-select').value, oldWorkflow=$('#workflow-type').value;
    const result=await Promise.all([api('Bootstrap'),api('List')]); state=result[0];requests=result[1];
    $('#contract-select').innerHTML=options(state.contracts); $('#contract-select').value=oldContract;
    $('#submit-type').innerHTML=options(state.types.filter(t=>t.IsActive)); $('#submit-type').value=oldType;
    if(state.manager) {
        $('#component-select').innerHTML=options(state.components,'بدون اتصال به مؤلفه');
        $('#workflow-type').innerHTML=options(state.types); $('#workflow-type').value=oldWorkflow;
        renderTypes();
    }
    renderRequests();updatePeriod();enhanceSelects();page(currentPage||(state.manager?'dashboard':'submit'));
}
function editType(id) {
    const t=state.types.find(t=>t.Id===id), form=$('#type-form'); form.reset();
    ['id','title','description','componentId','monthOffset'].forEach(k=>{const field={id:'Id',title:'Title',description:'Description',componentId:'ComponentId',monthOffset:'MonthOffset'}[k]; form.elements[k].value=t[field]==null?'':t[field];});
    form.elements.version.value=version(t.Version); form.elements.isActive.checked=t.IsActive;
    $('#type-form-title').textContent='ویرایش تعریف مدرک';enhanceSelects();
}
function addStage(stage,users) {
    stage=stage||{Title:'',RequireAll:true};users=users||[];
    const el=document.createElement('div');el.className='stage';
    el.innerHTML='<div class="stage-top"><strong class="stage-number"></strong><button type="button" class="secondary remove-stage">حذف مرحله</button></div><div class="form-grid"><label>عنوان مرحله<input class="stage-title" maxlength="100" placeholder="مثلاً بررسی مشاور" value="'+escape(stage.Title)+'"/></label><label>قاعده تأیید<select class="stage-mode"><option value="all">تأیید همه افراد لازم است</option><option value="any">تأیید یک نفر کافی است</option></select></label></div><input class="reviewer-search" placeholder="جست‌وجوی تأییدکننده…" aria-label="جست‌وجوی تأییدکننده"/><div class="reviewer-grid">'+state.users.map(u=>'<label class="check"><input type="checkbox" value="'+u.Id+'" '+(users.includes(u.Id)?'checked':'')+'/>'+escape(u.Title)+' <small>#'+u.Id+'</small></label>').join('')+'</div>';
    el.querySelector('.stage-mode').value=stage.RequireAll?'all':'any';
    el.querySelector('.remove-stage').onclick=()=>{el.remove();renumber();};
    el.querySelector('.reviewer-search').oninput=e=>el.querySelectorAll('.reviewer-grid label').forEach(l=>l.hidden=!l.textContent.includes(e.target.value));
    $('#stages').appendChild(el);renumber();enhanceSelects();
}
function renumber() { root.querySelectorAll('.stage-number').forEach((e,i)=>e.textContent='مرحله '+number(i+1)); }
async function loadWorkflow() {
    const sequence=++workflowLoad; $('#stages').innerHTML=''; workflowVersion='';
    const id=$('#workflow-type').value;if(!id)return;
    const data=await api('Workflow?id='+id);if(sequence!==workflowLoad)return;
    workflowVersion=version(data.version[0].Version);
    data.stages.forEach(s=>addStage(s,data.users.filter(u=>u.StageId===s.Id).map(u=>u.UserId)));
    if(!data.stages.length)addStage();
}
function fileLinks(files) {return '<div class="file-links">'+files.map(f=>'<a class="secondary" href="'+base+'Download?id='+f.Id+'">↓ '+escape(f.FileName)+'</a>').join('')+'</div>';}
async function detail(id) {
    const d=await api('Detail?id='+id);currentDetail=d;const r=d.request;
    let html='<h2>'+escape(r.TypeTitle)+'</h2><p class="muted">'+escape(r.ContractTitle)+' · دوره '+number(r.PeriodYear)+' / '+number(r.PeriodMonth)+' · پرونده '+number(r.Id)+'</p><div class="detail-summary">'+badge(r.Status)+'<span class="tag">نسخه '+number(r.Revision)+'</span><span class="tag">مبلغ نهایی: '+(r.ApprovedAmount==null?'ثبت نشده':money(r.ApprovedAmount)+' ریال')+'</span></div>';
    html+='<div class="step-line">'+d.steps.map(s=>'<div class="step-item '+(r.CurrentStep===s.Position?'current':'')+'"><strong>'+number(s.Position)+' · '+escape(s.Title)+'</strong><small>'+(s.RequireAll?'تأیید همه':'تأیید یک نفر')+'</small><small>'+d.reviewers.filter(u=>u.Position===s.Position).map(u=>escape(u.Name)).join('، ')+'</small></div>').join('')+'</div><h3>نسخه‌ها، نظرات و پیوست‌ها</h3>';
    d.revisions.forEach(v=>{
        html+='<div class="timeline-item"><strong>بارگذاری نسخه '+number(v.Revision)+'</strong><small> · '+escape(v.ActorName)+' · '+date(v.CreatedUtc)+'</small><p>'+escape(v.Description)+'</p>'+fileLinks(d.files.filter(f=>f.Revision===v.Revision&&f.ReviewId==null))+'</div>';
        d.reviews.filter(x=>x.Revision===v.Revision).forEach(x=>{
            html+='<div class="timeline-item"><strong>'+escape(x.ReviewerName)+' · '+decisions[x.Decision]+'</strong><small> · مرحله '+number(x.Step)+' · '+date(x.CreatedUtc)+'</small><p>'+escape(x.Description)+'</p><p class="muted">مبلغ مورد تأیید: '+(x.Amount==null?'ثبت نشده':money(x.Amount)+' ریال')+'</p>'+fileLinks(d.files.filter(f=>f.ReviewId===x.Id))+'</div>';
        });
    });
    if(d.canReview) html+='<form id="review-form" class="detail-form"><h3>ثبت نتیجه بررسی</h3><div class="form-grid"><label>نتیجه<select name="decision"><option value="1">تأیید</option><option value="2">تأیید مشروط</option><option value="0">عودت برای اصلاح</option></select></label><label>مبلغ مورد تأیید (ریال؛ اختیاری)<input type="number" name="amount" min="0" max="9999999999999999" step="0.01"/></label></div><label>شرح نظر / شروط / علت عودت<textarea name="description" rows="3" required maxlength="4000"></textarea></label><label>فایل بررسی (اختیاری؛ حداکثر ۱ مگابایت)<input name="file" type="file" accept=".pdf,.png,.jpg,.jpeg,.xlsx"/></label><button type="submit" class="primary">ثبت نتیجه بررسی</button></form>';
    if(d.canEdit) html+='<form id="resubmit-form" class="detail-form"><h3>اصلاح و ارسال نسخه جدید</h3><p class="muted">نسخه قبلی حفظ می‌شود. اکسل و پیوست کامل نسخه جدید را بارگذاری کنید؛ پیوست قبلی خودکار منتقل نمی‌شود. بررسی از مرحله اول آغاز خواهد شد.</p><label>شرح اصلاحات<textarea name="description" required maxlength="4000" rows="3"></textarea></label><div class="form-grid"><label>اکسل اصلاحی (الزامی؛ حداکثر ۱ مگابایت)<input name="excel" type="file" required accept=".xlsx"/></label><label>پیوست نسخه جدید (حداکثر ۱ مگابایت)<input name="attachment" type="file" accept=".pdf,.png,.jpg,.jpeg,.xlsx"/></label></div><button type="submit" class="primary">ارسال مجدد برای بررسی</button></form>';
    $('#detail-body').innerHTML=html;enhanceSelects();
    if(!$('#detail-dialog').open)$('#detail-dialog').showModal();
    if($('#review-form')) $('#review-form').onsubmit=e=>saveDetail(e,'Review');
    if($('#resubmit-form')) $('#resubmit-form').onsubmit=e=>saveDetail(e,'Resubmit');
}
function validateFiles(form) {for(const input of form.querySelectorAll('input[type=file]'))if(input.files[0]&&input.files[0].size>1048576)throw new Error('حداکثر اندازه هر فایل ۱ مگابایت است.');}
function saveDetail(event,action) {
    event.preventDefault();const form=event.target, button=form.querySelector('button[type=submit]');
    attempt(async()=>{
        validateFiles(form); const r=currentDetail.request, data=new FormData(form);data.set('id',r.Id);data.set('revision',r.Revision);data.set('step',r.CurrentStep);
        await api(action,data);$('#detail-dialog').close();await refresh();notice('عملیات با موفقیت ثبت شد.');
    },button);
}
root.addEventListener('click',event=>{
    const nav=event.target.closest('[data-page]');if(nav){event.preventDefault();page(nav.dataset.page);}
    const view=event.target.closest('[data-detail]');if(view)attempt(()=>detail(Number(view.dataset.detail)),view);
    const edit=event.target.closest('[data-edit-type]');if(edit)editType(Number(edit.dataset.editType));
});
$('#close-detail').onclick=()=>$('#detail-dialog').close();
$('#refresh').onclick=e=>attempt(refresh,e.currentTarget);
$('#search').oninput=renderRequests;$('#status-filter').onchange=renderRequests;
$('#submit-type').onchange=updatePeriod;$('#contract-select').onchange=updatePeriod;
$('#new-type').onclick=()=>{$('#type-form').reset();$('#type-form').elements.id.value='0';$('#type-form').elements.version.value='';$('#type-form-title').textContent='تعریف مدرک جدید';};
$('#type-form').onsubmit=e=>{
    e.preventDefault();const form=e.target;
    attempt(async()=>{validateFiles(form);const data=new FormData(form);data.set('isActive',form.elements.isActive.checked);await api('SaveType',data);form.reset();form.elements.id.value='0';form.elements.version.value='';$('#type-form-title').textContent='تعریف مدرک جدید';await refresh();notice('تعریف مدرک ذخیره شد. مراحل تأیید آن را نیز تنظیم کنید.');},form.querySelector('button[type=submit]'));
};
$('#workflow-type').onchange=()=>attempt(loadWorkflow);
$('#add-stage').onclick=()=>{if(!$('#workflow-type').value){notice('ابتدا نوع مدرک را انتخاب کنید.',true);return;}if(root.querySelectorAll('.stage').length>=20){notice('حداکثر ۲۰ مرحله مجاز است.',true);return;}addStage();};
$('#save-workflow').onclick=e=>attempt(async()=>{
    const stages=Array.from(root.querySelectorAll('.stage')).map(el=>({Title:el.querySelector('.stage-title').value.trim(),RequireAll:el.querySelector('.stage-mode').value==='all',Users:Array.from(el.querySelectorAll('.reviewer-grid input:checked')).map(x=>Number(x.value))}));
    if(!stages.length||stages.some(s=>!s.Title||!s.Users.length))throw new Error('هر مرحله باید عنوان و حداقل یک تأییدکننده داشته باشد.');
    const data=new FormData();data.set('typeId',$('#workflow-type').value);data.set('version',workflowVersion);data.set('stagesJson',JSON.stringify(stages));await api('SaveWorkflow',data);await refresh();await loadWorkflow();notice('گردش تأیید ذخیره شد؛ پرونده‌های قبلی بدون تغییر باقی ماندند.');
},e.currentTarget);
$('#submit-form').onsubmit=e=>{
    e.preventDefault();const form=e.target;
    attempt(async()=>{validateFiles(form);await api('Submit',new FormData(form));form.reset();await refresh();page('tracking');notice('مدرک ثبت و برای بررسی ارسال شد.');},form.querySelector('button[type=submit]'));
};

// Dependency-free searchable select. Keep the native element as the form value source.
const selectWidgets = new WeakMap();
let selectorId = 0;
let activeDropdown = null;
function positionDropdown() {
    if(!activeDropdown)return;
    const {menu,button,list}=activeDropdown;
    if(!button.isConnected || !button.getClientRects().length){closeSelects();return;}
    const rect=button.getBoundingClientRect(), viewport=window.visualViewport;
    const width=viewport?viewport.width:window.innerWidth, height=viewport?viewport.height:window.innerHeight;
    const offsetX=viewport?viewport.offsetLeft:0, offsetY=viewport?viewport.offsetTop:0;
    const popupWidth=Math.min(Math.max(rect.width,200),width-16);
    const below=offsetY+height-rect.bottom-12, above=rect.top-offsetY-12;
    const upwards=below<240 && above>below;
    list.style.maxHeight=Math.max(40,Math.min(200,(upwards?above:below)-64))+'px';
    menu.style.width=popupWidth+'px';
    menu.style.left=Math.max(offsetX+8,Math.min(rect.right-popupWidth,offsetX+width-popupWidth-8))+'px';
    menu.style.top=(upwards?Math.max(offsetY+8,rect.top-menu.getBoundingClientRect().height-5):rect.bottom+5)+'px';
}
window.addEventListener('resize',positionDropdown);
document.addEventListener('scroll',event=>{
    if(activeDropdown && !activeDropdown.menu.contains(event.target))positionDropdown();
},true);
if(window.visualViewport){window.visualViewport.addEventListener('resize',positionDropdown);window.visualViewport.addEventListener('scroll',positionDropdown);}
$('#detail-dialog').addEventListener('close',()=>closeSelects());
const normalizeSearch = value => String(value).replace(/ي/g,'ی').replace(/ك/g,'ک').replace(/[\u200c\u200f]/g,' ').trim().toLocaleLowerCase('fa');
function closeSelects(except) {
    root.querySelectorAll('.df-select.open').forEach(w=>{
        if(w===except)return;
        const menu=w.querySelector('.df-select-menu');
        if(typeof menu.hidePopover==='function' && menu.matches(':popover-open'))menu.hidePopover();
        w.classList.remove('open');menu.hidden=true;
        if(activeDropdown && activeDropdown.menu===menu)activeDropdown=null;
        root.querySelectorAll('.df-select-raised').forEach(el=>el.classList.remove('df-select-raised'));
        w.querySelector('.df-select-toggle').setAttribute('aria-expanded','false');
    });
}
function enhanceSelects() {
    root.querySelectorAll('select').forEach(select=>{
        if(selectWidgets.has(select)){selectWidgets.get(select).sync();return;}
        const wrapper=document.createElement('div');wrapper.className='df-select';
        select.parentNode.insertBefore(wrapper,select);wrapper.appendChild(select);
        select.classList.add('df-native-select');select.tabIndex=-1;
        const button=document.createElement('button');button.type='button';button.className='df-select-toggle';
        button.innerHTML='<span></span><b aria-hidden="true">⌄</b>';
        button.setAttribute('aria-haspopup','dialog');button.setAttribute('aria-expanded','false');
        const label=select.closest('label');
        const fieldName=select.getAttribute('aria-label')||(label?Array.from(label.childNodes).filter(n=>n.nodeType===3).map(n=>n.textContent).join(' ').trim():'انتخاب گزینه');
        button.setAttribute('aria-label',fieldName);
        const menu=document.createElement('div');menu.className='df-select-menu';menu.hidden=true;menu.id='df-options-'+(++selectorId);
        const topLayer=typeof menu.showPopover==='function';
        if(topLayer)menu.setAttribute('popover','manual');
        menu.setAttribute('role','dialog');menu.setAttribute('aria-label','انتخاب '+fieldName);button.setAttribute('aria-controls',menu.id);
        const search=document.createElement('input');search.type='search';search.className='df-select-search';search.placeholder='جست‌وجو در گزینه‌ها…';search.autocomplete='off';search.setAttribute('aria-label','جست‌وجوی '+fieldName);
        const list=document.createElement('div');list.className='df-select-options';list.setAttribute('role','listbox');list.setAttribute('aria-label',fieldName);
        menu.append(search,list);wrapper.append(button,menu);
        function sync(){button.querySelector('span').textContent=select.selectedOptions.length?select.selectedOptions[0].textContent:'انتخاب کنید';button.disabled=select.disabled;button.setAttribute('aria-required',String(select.required));}
        function paint(){
            list.replaceChildren();const term=normalizeSearch(search.value);
            Array.from(select.options).forEach(option=>{
                if(option.hidden||option.disabled||!normalizeSearch(option.textContent).includes(term))return;
                const choice=document.createElement('button');choice.type='button';choice.className='df-select-option';choice.textContent=option.textContent;
                choice.setAttribute('role','option');choice.setAttribute('aria-selected',String(option.selected));
                choice.onclick=event=>{event.preventDefault();select.value=option.value;sync();closeSelects();select.dispatchEvent(new Event('change',{bubbles:true}));button.focus();};
                list.appendChild(choice);
            });
            if(!list.children.length){const empty=document.createElement('div');empty.className='df-select-empty';empty.textContent='گزینه‌ای پیدا نشد';empty.setAttribute('role','status');list.appendChild(empty);}
        }
        function open(){
            closeSelects(wrapper);sync();search.value='';paint();menu.hidden=false;
            wrapper.classList.add('open');button.setAttribute('aria-expanded','true');
            if(topLayer){
                menu.showPopover();activeDropdown={menu,button,list};positionDropdown();
            }else{
                // Older browsers: raise the ancestor stacking contexts, not only the menu.
                for(let parent=wrapper.parentElement;parent && parent!==root;parent=parent.parentElement){
                    if(parent.tagName!=='DIALOG')parent.classList.add('df-select-raised');
                }
            }
            search.focus({preventScroll:true});
        }
        button.onclick=event=>{event.preventDefault();if(wrapper.classList.contains('open'))closeSelects();else open();};
        button.onkeydown=event=>{if(event.key==='ArrowDown'||event.key==='ArrowUp'){event.preventDefault();open();}};
        search.oninput=()=>{paint();if(topLayer)positionDropdown();};
        menu.addEventListener('keydown',event=>{
            const items=Array.from(list.querySelectorAll('button'));let index=items.indexOf(document.activeElement);
            if(event.key==='Escape'){event.preventDefault();event.stopPropagation();closeSelects();button.focus();}
            if(event.key==='ArrowDown'||event.key==='ArrowUp'){event.preventDefault();if(items.length)items[(index+(event.key==='ArrowDown'?1:-1)+items.length)%items.length].focus();}
            if(event.key==='Enter'&&event.target===search){event.preventDefault();if(items.length)items[0].click();}
        });
        wrapper.addEventListener('focusout',()=>setTimeout(()=>{if(!wrapper.contains(document.activeElement))closeSelects();},0));
        select.addEventListener('change',sync);
        select.addEventListener('invalid',event=>{event.preventDefault();notice('لطفاً '+fieldName+' را انتخاب کنید.',true);open();});
        selectWidgets.set(select,{sync,open});sync();
    });
}
document.addEventListener('click',event=>{if(!event.target.closest('#df-app .df-select'))closeSelects();});
root.addEventListener('reset',()=>setTimeout(enhanceSelects,0));
root.querySelectorAll('[data-panel]').forEach(panel=>{panel.id='df-panel-'+panel.dataset.panel;panel.setAttribute('role','tabpanel');panel.setAttribute('aria-labelledby',panel.dataset.panel==='inbox'?'inbox-nav':'df-tab-'+panel.dataset.panel);});
root.querySelectorAll('.nav-item').forEach(tab=>{tab.id=tab.id||'df-tab-'+tab.dataset.page;tab.setAttribute('aria-controls','df-panel-'+tab.dataset.page);});
$('.top-tabs').addEventListener('keydown',event=>{
    if(!['ArrowRight','ArrowLeft','Home','End'].includes(event.key))return;
    const tabs=Array.from(root.querySelectorAll('.nav-item')).filter(tab=>!tab.hidden);const index=tabs.indexOf(event.target);if(index<0)return;
    event.preventDefault();let next=event.key==='Home'?0:event.key==='End'?tabs.length-1:(index+(event.key==='ArrowLeft'?1:-1)+tabs.length)%tabs.length;
    tabs[next].focus();page(tabs[next].dataset.page);
});

$('#today').textContent=new Intl.DateTimeFormat('fa-IR',{dateStyle:'long',timeZone:'Asia/Tehran'}).format(new Date());
enhanceSelects();
attempt(refresh);
})();
