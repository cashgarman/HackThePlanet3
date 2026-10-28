"""Rebuild the authored Vesper v4 catalog from the preserved v3 source definitions."""
import copy,json,math,uuid,zipfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]; DATA=ROOT/'data'; ARCHIVE=ROOT/'Docs/LegacyV3/catalog.zip'
if not ARCHIVE.exists():
 ARCHIVE.parent.mkdir(parents=True,exist_ok=True)
 with zipfile.ZipFile(ARCHIVE,'w',zipfile.ZIP_DEFLATED) as z:
  for p in DATA.rglob('*.json'): z.write(p,p.relative_to(DATA).as_posix())
source_zip=zipfile.ZipFile(ARCHIVE)
def old(p): return json.loads(source_zip.read(p))
def save(p,d):
 p=DATA/p;p.parent.mkdir(parents=True,exist_ok=True);p.write_text(json.dumps(d,indent=2)+'\n',encoding='utf-8')
def const(v):return {'type':'ConstantNumber','value':v}
def mod(stat,v,op='Multiply',when=None):return {'id':'','stat':stat,'priority':0,'operation':op,'when':when or {'type':'AlwaysCondition'},'value':const(v)}
def act(a):return {'type':'ActionCondition','action':a,'tag':''}
manifest=old('manifest.json'); replace={'Contracts','Technologies','Modules','Chassis','Companies','Networks'}
entries=[e for e in manifest['entries'] if e['category'] not in replace]
for e in manifest['entries']:
 if e['category'] in replace:
  p=(DATA/e['file']).resolve();assert DATA.resolve() in p.parents
  if p.exists():p.unlink()
def register(cat,typ,path,d):
 save(path,d);entries.append({'key':uuid.uuid5(uuid.NAMESPACE_URL,'vesper-v4/'+path).hex,'category':cat,'file':path,'type':typ})
game=old('game/game.json')
# New tree: five ranks in each discipline, four map utilities, four hybrids.
branches=[('RECON',['wide','signal','blueprint','credential-analysis','data-engineering'],['Rangefinder','Signal Fingerprint','Branch Map','Cipher Autopsy','Forensic Overlay']),('INTRUSION',['efficient','chain','selective','protocol-spoof','parallel-engineering'],['Lean Payload','Task Weaver','Mirror Strike','Credential Fork','Vector Engineering']),('STEALTH',['quiet','ghost','backdoor','loop-feed','signal-engineering'],['Low Profile','Ghost Lane','Dead Address','False Echo','Veil Engineering']),('CONTROL',['disrupt','blackout','decoy','hard-reset','infrastructure-2'],['Soft Jammer','Dark Interval','Trace Sink','Controller Capture','Countermeasure Engineering'])]
techs=[];base=old('technologies/base.json');base.update(title='Field Kit',description='Your private workstation. Scan, establish access, recover evidence and leave with a plan.');techs.append(base)
for bi,(branch,ids,names) in enumerate(branches):
 for i,(id,name) in enumerate(zip(ids,names)):
  d=old('technologies/'+id+'.json');d.update(title=name,cost=[45,70,95,125,160][i],parents=['base' if i==0 else ids[i-1]],position={'x':(bi-1.5)*320,'y':(i+1)*155})
  techs.append(d)
for i in range(4):
 d=old('technologies/waypoints-'+str(i+1)+'.json');d.update(cost=[35,65,95,125][i],position={'x':850,'y':i*155});techs.append(d)
for i,(id,name,parents) in enumerate([('bridge','Quiet Pivot',['wide','efficient']),('weakpoint','Forensic Breach',['signal','selective']),('buffer','Blind Suppression',['quiet','blackout']),('cascade','Controlled Backdoor',['ghost','disrupt'])]):
 d=old('technologies/'+id+'.json');d.update(title=name,cost=[140,160,180,200][i],parents=parents,position={'x':(i-1.5)*330,'y':1000});techs.append(d)
for d in techs:
 if d['id']=='credential-analysis': d.update(special=False,modifiers=[mod('duration',.65,when=act('extract-key'))],description='Extract credentials 35% faster. Core extraction is available to every operator.')
 if d['id']=='data-engineering': d['modifiers']=[mod('duration',.8,when=act('download'))];d['description']='Recover evidence and archives 20% faster; unlock Cipher Lens hardware.'
 if d['id']=='parallel-engineering': d['modifiers']=[mod('compute',.15,'Add')];d['description']='+0.15 compute and access to the Mirror Core and Credential Loom.'
 if d['id']=='signal-engineering': d['modifiers']=[mod('watcherTrace',-.25,'Add')];d['description']='25% less monitored-link trace; unlock Veil Mesh and Watcher Baffle.'
for d in techs:register('Technologies','TechnologyDefinition','technologies/'+d['id']+'.json',d)
game['technologies']=[d['id'] for d in techs]
# Rig models keep semantic identifiers to keep extension hooks useful, while replacing their presentation and tuning.
models=[('processor','Salvage Vector CPU',1,300),('accelerator','Burst ASIC',2,1000),('uplink','Folded Antenna',1,250),('recon-cache','Survey Cache',2,750),('drive','Cold Archive',1,300),('storage-array','Evidence Vault',2,900),('ram','Working Memory',1,300),('ram-bank','Session Bank',2,850),('recycler','Reclaim Controller',1,400),('quiet-filter','Noise Comb',2,800),('decoder-board','Cipher Lens',2,900),('spoof-asic','Credential Loom',3,1600),('veil-array','Veil Mesh',3,1500),('isolation-bank','Isolation Matrix',3,1700),('power-1','Cell Supply',1,450),('power-2','Trunk Supply',2,1000),('power-3','District Supply',3,2000),('cooling-1','Fin Cooler',1,250),('cooling-2','Loop Cooler',2,700),('cooling-3','Cryogenic Loop',3,1600)]
mods=[]
for id,name,tier,price in models:
 d=old('rig/modules/'+id+'.json');d.update(title=name,tier=tier,price=price,flavour='PATCHBAY / Vesper salvage exchange. Every component has a previous owner.')
 for req in d['requirements']:
  c=req['condition']
  if c.get('technology')=='infrastructure-3':c['technology']='infrastructure-2'
 mods.append(d)
for id,name,stat,value,price,parent in [('watcher-baffle','Watcher Baffle','watcherTrace',-.35,1000,'signal-engineering'),('mirror-core','Mirror Core','bandwidth',.55,1800,'parallel-engineering')]:
 d=copy.deepcopy(mods[0]);d.update(id=id,title=name,description=('35% less Watcher traversal trace.' if id=='watcher-baffle' else '+0.55 GB/s transfer bandwidth; pair with adjacent processing.'),tier=2 if price<=1100 else 3,price=price,width=2,height=1,powerDemand=5,heatOutput=4,modifiers=[mod(stat,value,'Add')],requirements=[{'condition':{'type':'ResearchCondition','technology':parent,'equipped':False},'message':'Requires '+parent+'.'}]);mods.append(d)
mods[-2].update(branch='STEALTH',family='stealth')
for d in mods:register('Modules','RigModuleDefinition','rig/modules/'+d['id']+'.json',d)
game['modules']=[d['id'] for d in mods]
for i,price in enumerate([0,1200,2800,5400]):
 d=old('rig/chassis/chassis-'+str(i+1)+'.json');d.update(title=['Suitcase','Workbench','Rackframe','Switchroom'][i],price=price,requiredJobs=[0,1,4,8][i],slots=i+2,description='Vesper field chassis. More room, ability slots and resource headroom.');register('Chassis','RigChassisDefinition','rig/chassis/'+d['id']+'.json',d)
# Three front companies; their relationship is only disclosed in debriefs.
for i,(name,color) in enumerate([('HALDEN GRID','#E9BB60'),('ASTER REGISTRY','#66DDCC'),('NULLPOINT SECURITY','#AC88E9')]):
 d=old('companies/company-'+str(i)+'.json');d['title']=name;register('Companies','CompanyDefinition','companies/'+d['id']+'.json',d)
# Six visibly different authored networks. Two ingress paths, cross-links, controller and optional archive detours.
patterns=['exfiltrate','alter','reconstruct','intercept','evacuate','seize']
networks=[]
for pi,pattern in enumerate(patterns):
 d=old('networks/corporate-network.json');d.update(id='vesper-'+pattern,title=pattern.title()+' / Vesper',layout='Authored',preferredNode='n22',previewNode='n22',links=[])
 roles=[('home','gateway','Your gateway',-1,0),('n1','workstation','Public service terminal',0,0),('n2','relay','Maintenance ingress',0,1),('n3','firewall','Fast access gate',1,0),('n4','relay','Service crawlspace',1,1),('n5','workstation','Dispatch console',2,0),('n6','relay','Neighborhood exchange',2,1),('n7','scanner','Traffic sensor',1,-1),('n8','firewall','Archive partition',3,0),('n9','database','Signed dispatch record',3,1),('n14','securityhub','Response controller',2,-1),('n19','workstation','Credential witness',1,2),('n20','database','Live mirror',4,1),('n22','database','Authoritative record',4,0),('n10','database','Staff correspondence',2,2),('n11','database','Maintenance diary',3,-1),('n12','database','Unfiled testimony',4,2)]
 d['nodes']=[]
 for id,typ,label,col,lane in roles:
  angle=pi*math.pi/9;x=col*5-8;y=lane*4;z=math.sin(col+pi)*2
  d['nodes'].append({'id':id,'type':typ,'label':label,'detail':'VESPER / '+pattern.upper()+' subnet. Routes share dependencies; inspect the controller before committing.','column':col,'row':lane,'x':100+col*120,'y':180+lane*70,'position':{'x':x*math.cos(angle)-z*math.sin(angle),'y':y,'z':x*math.sin(angle)+z*math.cos(angle)},'known':id=='home','controlled':id=='home','bonus':id in ['n10','n11','n12'],'loreIndex':['n10','n11','n12'].index(id) if id in ['n10','n11','n12'] else -1,'securityModifiers':[mod('security',1.5 if typ=='firewall' else 1,'Override')]})
 edges=[('home','n1'),('home','n2'),('n1','n3'),('n3','n5'),('n5','n8'),('n8','n22'),('n2','n4'),('n4','n6'),('n6','n9'),('n9','n20'),('n20','n22'),('n4','n19'),('n19','n9'),('n1','n7'),('n7','n14'),('n14','n5'),('n6','n10'),('n8','n11'),('n20','n12')]
 if pi%2:edges += [('n5','n9'),('n19','n14')]
 else:edges += [('n4','n5'),('n9','n11')]
 d['links']=[{'a':a,'b':b,'when':{'type':'AlwaysCondition'}} for a,b in edges];d['payloadStorage']=const(16);d['archiveStorage']=const(8)
 for n in d['nodes']:
  if n['id']!='home':
   variable=mod('security',1,'Override',{'type':'CompareCondition','comparison':'GreaterOrEqual','left':{'type':'InputNumber','input':'Difficulty','key':'','filter':{'type':'AlwaysCondition'}},'right':const(2)})
   variable['value']={'type':'RandomNumber','minimum':2 if n['type']=='firewall' else 1,'maximum':3 if n['type']=='firewall' else 2};variable['priority']=1;n['securityModifiers'].append(variable)
 d['links'].append({'a':'n2','b':'n19','when':{'type':'CompareCondition','comparison':'Equal','left':{'type':'BinaryNumber','operation':'Modulo','left':{'type':'InputNumber','input':'Seed','key':'','filter':{'type':'AlwaysCondition'}},'right':const(2)},'right':const(0)}})

 register('Networks','NetworkDefinition','networks/'+d['id']+'.json',d);networks.append(d['id'])
game['networks']=networks
# Mission action variants expose the actual objective instead of relabeling every task as a download.
for id,title in [('alter-record','Synchronize records'),('intercept','Intercept transfer'),('evacuate','Open evacuation channel')]:
 d=old('actions/download.json');d.update(id=id,title=title,shortcut='None',shortcutLabel='',legacyGroup='',presentationPriority=1)
 d['description']={'alter-record':'Commit a matching signed replacement to both records. The mirror purge begins on the first write.','intercept':'Capture the live transfer, then secure its receipt before the purge window closes.','evacuate':'Export the protected identity through a controlled response hub. Keep the route open until extraction.'}[id]
 if id=='evacuate':d['requirements'].append({'condition':{'type':'NamedNodeCondition','nodeId':'n14','condition':{'type':'NodeStateCondition','state':'Controlled'}},'message':'Capture the response controller to open a protected evacuation route.'})
 save('actions/'+id+'.json',d);entries.append({'key':uuid.uuid5(uuid.NAMESPACE_URL,id).hex,'category':'Actions','file':'actions/'+id+'.json','type':'ActionDefinition'});game['actions'].append(id)
# Core credential extraction is always available; specialization offers faster or remote alternatives.
a=old('actions/extract-key.json');a['initiallyAvailable']=True;save('actions/extract-key.json',a)
# Story catalog, with authored motives, evidence, and consequences.
opening=[('ghost-meter','Ghost Meter','exfiltrate','An occupied apartment is billed for power it never received. MOTH offers payment for the original meter certificate.','The meter reports a deliberate disconnection, signed hours before the outage.'),('redacted-hours','Redacted Hours','alter','A dispatcher asks you to restore the missing minutes in a service ledger and its mirror.','Three unrelated failures share the same authorization token. The city was following a schedule.'),('unlisted-operator','Unlisted Operator','reconstruct','Find the signed dispatches behind a technician who appears in payroll but in no identity register.','The technician was real. Their identity was removed after they refused an emergency rehearsal.'),('listening-room','The Listening Room','seize','Trace MOTH’s relay to a vacant switching office. Capture its controller and recover the listening log.','The emergency protocol has a name: Night Index. Three people contact you with incompatible plans for the same evidence.')]
arcs={
 'expose':('RHEA / public archivist', [('margin-notes','Margin Notes','reconstruct','Reconcile an insurance index with the original outage log.','Compensation was denied before the emergencies occurred.'),('sealed-hearing','Sealed Hearing','exfiltrate','Recover testimony from a hearing whose transcript was never published.','Every witness received a different explanation for the same missing hour.'),('borrowed-voice','Borrowed Voice','intercept','Intercept an editor’s scheduled confession and its delivery receipt.','MOTH is a shared identity. One of its authors died before your first contract.'),('public-interest','Public Interest','evacuate','Extract the witness registry before its custodians surrender it.','The archive’s sponsor owns a silent stake in Halden Grid.'),('proof-of-life','Proof of Life','alter','Replace fabricated consent with matching originals in both registers.','The Night Index scores neighborhoods by their willingness to abandon other neighborhoods.')]),
 'shelter':('SABLE / community grid engineer', [('cold-clinic','Cold Clinic','evacuate','Open a protected channel for a clinic’s patients and backup controller.','The clinic’s outage was a test of how long volunteers would share their batteries.'),('spare-capacity','Spare Capacity','seize','Capture a reserve controller that reports no spare power.','Its reserve is real. The allocation is being sold to an unnamed continuity fund.'),('names-in-copper','Names in Copper','reconstruct','Compare repair signatures to the list of people erased from service.','A dismissed engineer still signs repair orders through the shared identity MOTH.'),('island-mode','Island Mode','alter','Synchronize the community grid’s island-mode permits.','The emergency interlock can be broken without shutting down the city.'),('open-door','Open Door','intercept','Intercept the next forced reconnection order and its receipt.','A hidden office is mapping every independent route that volunteers create.')]),
 'command':('ORIN / Directorate defector', [('accession','Accession','seize','Capture a succession controller without destroying its chain of custody.','The controller recognizes a vacant seat that has been empty for eleven years.'),('black-budget','Black Budget','exfiltrate','Recover the invoices that keep the vacancy funded.','Three competitors pay the same unseen administrator.'),('dead-signature','Dead Signature','reconstruct','Reconstruct a signing chain attributed to an administrator who died years ago.','MOTH’s relay holds fragments of that administrator’s authorization.'),('proxy-war','Proxy War','intercept','Intercept a replacement key before it reaches the security contractor.','Your handler’s enemies were being funded by the same continuity reserve.'),('quorum','Quorum','alter','Rewrite the duplicated quorum register so the hidden office must acknowledge a human operator.','There is no single leader. The Directorate is a voting protocol distributed through vendors.')])}
contracts=[]
def choice(id,title,desc,flag):return dict(id=id,title=title,description=desc,flag=flag)
def make(id,title,pattern,brief,reveal,stage,prereq=None,flags=None,optional=False,ending=False):
 d=old('contracts/quiet-copy.json');d.update(id=id,title=title,description=brief,company='company-'+str(patterns.index(pattern)%3),network='vesper-'+pattern,client='MOTH / unverified relay',tag=pattern.upper()+' / VESPER',objective='',condition='All required objectives must be secured. Optional archives pay extra. Quiet bonus uses peak trace.',aftermath=reveal,revelation=reveal,requiredWins=0,prerequisites=prereq or [],requiredFlags=flags or [],offerDays=3 if optional else 0,optional=optional,isEnding=ending,choices=[],evidence={'title':'','question':'','clueNodes':[],'clues':[],'answers':[]},credits=const(700+stage*110),xp=const(70+stage*6),difficulty=const(1 if stage<3 else 2 if stage<9 else 3),seed=const(410+len(contracts)*17),paceMultiplier=(3 if stage<3 else 4 if stage<9 else 4.5) * (1 if pattern in ['exfiltrate','seize'] else .7),objectives=[],actionRules=[],defenses=[])
 action={'alter':'alter-record','intercept':'intercept','evacuate':'evacuate'}.get(pattern,'download')
 targets=['n22'] if pattern=='exfiltrate' else ['n22','n20'] if pattern in ['alter','intercept'] else ['n22','n9','n19'] if pattern=='reconstruct' else ['n22','n14']
 for target in targets:
  ishub=target=='n14';d['objectives'].append({'nodeId':target,'label':title+' / '+target,'actionLabel':'Capture controller' if ishub else {'alter':'Synchronize signed mirror','intercept':'Intercept transfer receipt','evacuate':'Evacuate protected identities','reconstruct':'Recover signed clue'}.get(pattern,'Recover evidence'),'action':'hack' if ishub else action,'complete':{'type':'NodeStateCondition','state':'Controlled' if ishub else 'Collected'},'actionRequirements':[],'payload':not ishub})
 labels={'n22':'Authoritative record','n20':'Live mirror','n19':'Credential witness','n9':'Signed dispatch record','n14':'Response controller'}
 for o in d['objectives']:o['label']=title+' / '+labels[o['nodeId']]
 d['objective']=' / '.join(o['actionLabel']+' at '+labels[o['nodeId']] for o in d['objectives'])
 if pattern=='reconstruct':
  d['evidence']={'title':title+' / Evidence case','question':'Which explanation fits all three signed records?','clueNodes':targets,'clues':['Dispatch signature: the authorization predates the public incident.','Mirror receipt: the same authorization crossed two supposedly independent vendors.','Witness register: the affected operator was removed after objecting to the rehearsal.'],'answers':[{'id':'accident','title':'An ordinary equipment failure caused the missing records.','correct':False,'feedback':'The authorization exists before the failure. Accidental damage does not explain the signed schedule.'},{'id':'insider','title':'A lone technician altered the records after the incident.','correct':False,'feedback':'The independent mirror signed the same token before the technician could have changed it.'},{'id':'rehearsal','title':'A coordinated rehearsal was authorized before the incident.','correct':True,'feedback':reveal}]}
 if stage>=3:d['defenses'].append({'id':id+'-watch','kind':'Watcher','sourceNodeId':'n14','targetNodeIds':['n3','n8'],'minimumDifficulty':1,'intervalSeconds':60,'traceBurst':6})
 if pattern in ['alter','intercept']:d['defenses'].append({'id':id+'-purge','kind':'PurgeTimer','sourceNodeId':'n14','targetNodeIds':['n22','n20'],'minimumDifficulty':1,'intervalSeconds':100 if stage<8 else 80,'traceBurst':0})
 if stage>=5:d['defenses'].append({'id':id+'-lock','kind':'CredentialLock','sourceNodeId':'n19','targetNodeIds':['n22'],'minimumDifficulty':1,'intervalSeconds':60,'traceBurst':0})
 if stage>=9:d['defenses'].append({'id':id+'-sweep','kind':'SecuritySweep','sourceNodeId':'n14','targetNodeIds':[],'minimumDifficulty':1,'intervalSeconds':65,'traceBurst':7})
 d['lore']=[{'id':id+'/'+str(i),'title':t,'company':'','contract':title,'text':txt} for i,(t,txt) in enumerate([('A resident’s note','“We kept the hallway light on because someone always came home late. The meter called the empty stairwell a success.”'),('The unapproved repair','A service worker routes a spare battery around the official queue. The repair is marked unauthorized; the patient survives.'),('A marginal annotation','A pencil note beside the compliance stamp reads: If this is only a rehearsal, why do the names stay missing?')])]
 contracts.append(d);return d
prev=[]
for i,row in enumerate(opening):d=make(*row,i,prev);prev=[d['id']]
d['choices']=[choice(a,'Ally with '+client.split(' / ')[0],{'expose':'Build a public evidence chain. Opens the Exposure arc and closes the other alliances.','shelter':'Build independent service routes. Opens the Shelter arc and closes the other alliances.','command':'Seize the command chain. Opens the Command arc and closes the other alliances.'}[a],a) for a,(client,_) in arcs.items()]
for a,(client,rows) in arcs.items():
 prev=['listening-room']
 for i,row in enumerate(rows):d=make(*row,4+i,prev,[a]);d['client']=client;prev=[d['id']]
# Shared crisis requires branch completion through an explicit flag granted by each fifth mission.
for a,(_,rows) in arcs.items():
 d=next(d for d in contracts if d['id']==rows[-1][0]);d['choices']=[choice('continue-'+a,'Follow the hidden channel','Your alliance has enough evidence to confront the Night Index rehearsal.', 'arc-complete')]
d=make('thirteenth-test','The Thirteenth Test','reconstruct','Recover the signed evaluation behind the city’s thirteenth emergency rehearsal.','The Directorate cultivated both its defenders and its opposition. Your contracts were part of a succession test.',9,[],['arc-complete'])
d=make('no-central-server','No Central Server','seize','Capture the fallback coordinator and obtain the dependency map.','There is no master switch. Publication, independent infrastructure and command seizure each attack a different dependency.',10,['thirteenth-test'])
for ai,(a,(client,_)) in enumerate(arcs.items()):
 id=a+'-threshold';d=make(id,{'expose':'The Unredacted City','shelter':'Load Bearing People','command':'The Empty Chair'}[a],'intercept','Intercept the fallback order before the Directorate disperses its signing keys.','Your allies can act, but the method will determine who carries the risk.',11,['no-central-server'],[a]);d['client']=client
 d['choices']=[choice(a+'-open','Act immediately','Move before the Directorate can regroup. The conclusion favors reach and accepts disruption.',a+'-open'),choice(a+'-careful','Protect the named people first','Use a slower release and preserve specific service routes. The conclusion favors protection over reach.',a+'-careful')]
 for careful in [False,True]:
  suffix='careful' if careful else 'open';title={'expose':'Daylight Protocol','shelter':'Islands of Light','command':'Human Quorum'}[a]+(' / Protected' if careful else ' / Unbound')
  reveal={'expose':'The Night Index is published with its signatures intact. People begin auditing the systems that decide their lives.','shelter':'Independent districts keep their own power and identity ledgers. The Directorate loses its monopoly on continuity.','command':'A human quorum takes the signing seats. The protocol must answer to named people whose decisions can be challenged.'}[a]+(' Your allies protected the witnesses before acting; some records escaped, but their people did not disappear.' if careful else ' The release reaches every district at once. It breaks the old consensus and leaves difficult repairs for the survivors.')
  d=make(a+'-ending-'+suffix,title,['exfiltrate','evacuate','seize'][ai],'Complete your alliance’s final operation against the distributed fallback system.',reveal,12,[id],[a,a+'-'+suffix],ending=True);d['client']=client
 for i,pattern in enumerate(['exfiltrate','alter','evacuate']):
  d=make(a+'-side-'+str(i+1),['Unpaid Overtime','The Spare Key','Someone Still Answers'][i]+' / '+a.title(),pattern,'An ally needs help away from the main operation. This offer closes in three career days.','A small service survives because someone chose to maintain it.',5+i,[arcs[a][1][i][0]],[a],optional=True);d['client']=client
for i,pattern in enumerate(['exfiltrate','alter','intercept','seize']):
 d=make('vesper-freelance-'+str(i),'Exchange / '+['Cold Copy','Mirror Repair','Transit Receipt','Quiet Custody'][i],pattern,'Verified escrow offers a fresh target. No faction commitment is required.','The exchange settles another job. The city keeps changing.',3+i)
 d.update(recurring=True,credits=const(650+i*130),xp=const(65+i*10),seed={'type':'BinaryNumber','operation':'Add','left':{'type':'BinaryNumber','operation':'Multiply','left':{'type':'InputNumber','input':'Day','key':'','filter':{'type':'AlwaysCondition'}},'right':const(71)},'right':const(700+i*19)})
case_details={
'unlisted-operator':(['Payroll lists technician 441 alive at 08:10.','Dispatch shows 441 rejecting a rehearsal order at 08:14.','The identity mirror deletes 441 at 08:15, signed by a supervisor.'],'The registry was deliberately changed after a refusal.'),
'margin-notes':(['The outage log records failure at 03:12.','The insurer approved a denial at 02:50 using the same event number.','A vendor receipt binds both records to a token issued the previous evening.'],'The outage and its denial were planned together.'),
'names-in-copper':(['Repair order 17 carries the signature of a dismissed engineer.','The same signature appears on MOTH relay packets.','The engineer’s physical badge was destroyed before those packets were sent.'],'MOTH is a shared relay identity rather than one physical operator.'),
'dead-signature':(['The administrator’s death certificate predates the signing event by eleven years.','Three vendors each retain a different fragment of the old credential.','A quorum receipt combines the fragments without a living administrator.'],'The old authority persists through a distributed quorum.'),
 'thirteenth-test':(['The Directorate’s budget funds both the response team and the resistance relay.','A test plan names your first contract before you accepted it.','Success criteria score the replacement operator, not the service outage.'],'The resistance and its handler were part of a succession test.')}
for d in contracts:
 if d['id'] in case_details:
  clues,conclusion=case_details[d['id']];d['evidence']['clues']=clues
  d['evidence']['answers'][0].update(title='The records disagree because of an ordinary clerical error.',feedback='A clerical error does not explain the matching signed authorizations and their order.')
  d['evidence']['answers'][1].update(title='A lone outsider forged every record after the event.',feedback='The independent receipt and earlier signatures rule out a single later forgery.')
  d['evidence']['answers'][2].update(title=conclusion,feedback=d['revelation'])
 for i,fragment in enumerate(d['lore']):
  fragment['title']=d['title']+' / '+['Unsent message','Custody note','Private annotation'][i]
  fragment['text']=[d['client']+': '+d['description']+' The sender asks you to keep the names attached to the evidence; anonymous totals have concealed people before.', 'Archive custody, '+d['title']+': '+d['revelation']+' A second copy carries a hand-written objection to the official disposition.', 'Personal note filed under '+d['id']+': '+d['aftermath']+' Someone has crossed out the word inevitable. No replacement was supplied.'][i]
for ci,d in enumerate(contracts):
 template=json.loads(json.dumps(json.loads((DATA/('networks/'+d['network']+'.json')).read_text())))
 template['id']='subnet-'+d['id'];template['title']=d['title']+' / subnet'
 last='home'
 for k in range(4+ci%4):
  id='duct-'+str(k);node=copy.deepcopy(template['nodes'][2]);node.update(id=id,label='Utility conduit '+str(k+1),bonus=False,loreIndex=-1,position={'x':-10+k*4,'y':-8-ci%3,'z':4+ci%5})
  template['nodes'].append(node);template['links'].append({'a':last,'b':id,'when':{'type':'AlwaysCondition'}});last=id
 template['links'].append({'a':last,'b':'n9','when':{'type':'AlwaysCondition'}})
 for n in template['nodes']:
  n['detail']=d['title']+' / '+n['detail']
 register('Networks','NetworkDefinition','networks/'+template['id']+'.json',template);game['networks'].append(template['id']);d['network']=template['id']
for d in contracts:register('Contracts','ContractDefinition','contracts/'+d['id']+'.json',d)
game['contracts']=[d['id'] for d in contracts]
# Shared pacing is explicit per contract; remove the old difficulty-based duration inflation.
game['modifiers']=[m for m in game['modifiers'] if not m.get('id','').startswith('career-pacing')]
game['modifiers'].append(mod('trace',.55))
save('game/game.json',game)
career=old('career/career.json');career.update(credits=500,welcome='VESPER RELAY ONLINE // MOTH has left a meter certificate in your inbox. Start with Ghost Meter. Read the signatures.');career['stats'].append({'id':'watcherTrace','value':0})
for o in career['outcomes']:o['dayAdvance']=1
save('career/career.json',career)
save('manifest.json',{'schemaVersion':1,'entries':entries})
print('Authored',sum(not d['optional'] and not d['recurring'] for d in contracts),'story,',sum(d['optional'] for d in contracts),'side,',sum(d['recurring'] for d in contracts),'repeatable;',len(techs),'tech;',len(mods),'modules')

