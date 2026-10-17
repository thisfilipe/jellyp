#!/usr/bin/env python3
"""Library isolation and actual native timer test on a marked disposable server.

Run smoke-local.py first, then this script against the SAME localhost:18096 fixture.
Creates a second generated library and artwork; applies only to the first library
through Jellyfin's native scheduled-task interval trigger, then restores its original.
Jellyfin 12.1 adds a minimum minute before interval triggers; allow about two minutes.
Never use with production. Credentials and private IDs are never printed.
"""
import argparse,base64,hashlib,json,subprocess,time,urllib.request,urllib.parse
from pathlib import Path
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--root',required=True,type=Path)
args=parser.parse_args();root=args.root.resolve()
assert (root/'.jellyp-smoke-root').is_file(),'Refusing unmarked root'
base='http://127.0.0.1:18096/'
auth='MediaBrowser Client="JellyP library fixture", Device="disposable", DeviceId="jellyp-library-fixture", Version="0.3"'
def request(path,body=None):
 data=None if body is None else body if isinstance(body,bytes) else json.dumps(body).encode()
 with urllib.request.urlopen(urllib.request.Request(base+path,data=data,headers={'Authorization':auth,'Content-Type':'application/json'}),timeout=60) as response:
  payload=response.read();return json.loads(payload) if payload and 'json' in response.headers.get('Content-Type','') else payload
login=request('Users/AuthenticateByName',{'Username':'jellyp-test-admin','Pw':(root/'test-password').read_text()})
auth+=', Token="'+login['AccessToken']+'"'
assert Path(request('System/Info')['ProgramDataPath']).resolve()==root/'data','Server/root mismatch'
plugin='0928e334-8971-4205-a455-f42a76527f9e'
configuration=request('Plugins/'+plugin+'/Configuration')
task=next(task for task in request('ScheduledTasks') if task['Key']=='JellyPOptimizeArtwork')
original_triggers=task['Triggers']
def save(value):request('Plugins/'+plugin+'/Configuration',value)
def triggers(value):request('ScheduledTasks/'+task['Id']+'/Triggers',value)
def wait_completed(previous):
 for _ in range(220):
  status=request('JellyP/Status');report=status['LastRun']
  if report.get('ExecutionId')!=previous and report.get('Phase') in ('Completed','Failed','Cancelled'):
   assert report['Phase']=='Completed',report.get('Message');return status
  time.sleep(.5)
 raise AssertionError('Native task did not complete')
try:
 triggers([])
 for old in request('JellyP/Status')['RecentRecords']:
  if old['State']=='Committed':
   assert Path(old['Source']['Path']).resolve().is_relative_to(root/'data')
   request('JellyP/Rollback/'+old['Id'],b'')
 media=root/'other-media/JellyP Other (2026)';media.mkdir(parents=True,exist_ok=True)
 subprocess.run(['ffmpeg','-v','error','-f','lavfi','-i','color=c=black:s=64x64:d=1','-c:v','libx264','-pix_fmt','yuv420p','-y',str(media/'JellyP Other (2026).mp4')],check=True)
 folders=request('Library/VirtualFolders')
 if not any(folder['Name']=='JellyP other fixture' for folder in folders):
  request('Library/VirtualFolders?name=JellyP%20other%20fixture&collectionType=movies&refreshLibrary=true',{'LibraryOptions':{'PathInfos':[{'Path':str(root/'other-media')}],'EnableRealtimeMonitor':False,'SaveLocalMetadata':False,'EnableInternetProviders':False,'TypeOptions':[{'Type':'Movie','MetadataFetchers':[],'ImageFetchers':[]}]}})
 for _ in range(120):
  items=request('Items?Recursive=true&IncludeItemTypes=Movie')['Items']
  if len(items)==2:break
  time.sleep(.25)
 assert len(items)==2,'Expected only the two generated fixtures'
 selected=next(item for item in items if item['Name']=='JellyP Canary (2026)')
 outside=next(item for item in items if item['Name']=='JellyP Other (2026)')
 # Reuse the restored synthetic PNG created by smoke-local.py, never external artwork.
 with urllib.request.urlopen(urllib.request.Request(base+'Items/'+selected['Id']+'/Images/Primary?format=png',headers={'Authorization':auth}),timeout=30) as response:png=response.read()
 assert png.startswith(b'\x89PNG')
 for item in (selected,outside):
  with urllib.request.urlopen(urllib.request.Request(base+'Items/'+item['Id']+'/Images/Primary',data=base64.b64encode(png),headers={'Authorization':auth,'Content-Type':'image/png'}),timeout=30) as response:response.read()
 outside_before=request('Items/'+outside['Id']+'/Images')
 library=next(folder for folder in request('Library/VirtualFolders') if folder['Name']=='JellyP fixture')
 config={**configuration,'ScopeMode':'libraries','IncludedLibraryIds':[library['ItemId']],'IncludedItemIds':[],
  'Enabled':True,'DryRun':False,'MaintenanceWindowConfirmed':False,'PreserveOriginals':True,'BatchSize':1,
  'MinimumFreeSpaceBytes':67108864,'Quality':85,'MinimumSsim':.8,'MinimumSavingsBytes':0,'MinimumSavingsRatio':0,
  'ImagePolicies':[{**policy,'Enabled':policy['Category']=='Primary','OverrideGlobal':False} for policy in configuration['ImagePolicies']]}
 save(config)
 before=request('JellyP/Status')['LastRun'].get('ExecutionId')
 # This is an actual Jellyfin timer, not an API call to JellyP/Run.
 triggers([{'Type':'IntervalTrigger','IntervalTicks':30000000}])
 applied=wait_completed(before)
 triggers([])
 assert applied['LastRun']['Converted']==1
 record=next(record for record in applied['RecentRecords'] if record['State']=='Committed')
 assert record['Source']['ItemId'] in (selected['Id'],library['ItemId']),'Conversion escaped selected library'
 assert request('Items/'+outside['Id']+'/Images')==outside_before,'Excluded library changed'
 # Simulation override must preserve the applying automation settings.
 preview=request('JellyP/Run?dryRun=true',b'')
 simulated=wait_completed(applied['LastRun']['ExecutionId'])
 assert simulated['LastRun']['ExecutionId']==preview['ExecutionId']
 assert simulated['LastRun']['Converted']==0 and simulated['LastRun']['Inventory']['AlreadyWebpImages']>=1
 assert request('Plugins/'+plugin+'/Configuration')['DryRun'] is False
 assert request('Plugins/'+plugin+'/Configuration')['Enabled'] is True
 # Restore explicitly even after setting automation back to preview.
 config.update(Enabled=False,DryRun=True);save(config)
 request('JellyP/Rollback/'+record['Id'],b'')
 restored=request('JellyP/Status')
 assert any(row['Id']==record['Id'] and row['State']=='RolledBack' for row in restored['RecentRecords'])
 assert hashlib.sha256(Path(record['Source']['Path']).read_bytes()).hexdigest().lower()==record['SourceHash'].lower()
 assert request('Items/'+outside['Id']+'/Images')==outside_before
 print('Passed: actual Jellyfin timer, selected-library conversion, excluded-library isolation, explicit empty/WebP outcome, independent manual simulation and exact restoration with preview automation.')
finally:
 triggers(original_triggers)
 save({**configuration,'Enabled':False,'DryRun':True,'MaintenanceWindowConfirmed':False})
