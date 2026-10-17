#!/usr/bin/env python3
"""Destructive only to a marked disposable Jellyfin fixture on localhost:18096. Never use with production.
Requires a running empty Jellyfin12.1 on localhost:18096, root/config/data/cache,
marker .jellyp-smoke-root and a one-second fixture video at media/JellyP Canary (2026)/JellyP Canary (2026).mp4.
Credentials stay in the disposable root with mode0600 and are never printed.
"""
import urllib.request,urllib.error,json,time,secrets,os,base64,struct,zlib,random
from pathlib import Path
import argparse
parser=argparse.ArgumentParser(description='End-to-end validation against an explicitly initialized local disposable Jellyfin server.')
parser.add_argument('--root', required=True, type=Path)
args=parser.parse_args()
root=args.root.resolve()
if not (root/'.jellyp-smoke-root').is_file(): raise SystemExit('Refusing to run without disposable-root marker .jellyp-smoke-root')
base='http://127.0.0.1:18096'; token=None
AUTH='MediaBrowser Client="JellyP isolated validation", Device="local fixture", DeviceId="jellyp-isolated", Version="0.1"'
def request(path,body=None,method=None,ctype='application/json'):
    headers={'Authorization':AUTH,'Content-Type':ctype}
    if token: headers['Authorization']=AUTH+', Token="'+token+'"'
    data=body if isinstance(body,bytes) else json.dumps(body).encode() if body is not None else None
    req=urllib.request.Request(base+'/'+path,data=data,method=method or ('POST' if data is not None else 'GET'),headers=headers)
    try:
        with urllib.request.urlopen(req,timeout=30) as res:
            b=res.read(); return json.loads(b) if b and 'json' in res.headers.get('Content-Type','') else b
    except urllib.error.HTTPError as e:
        print('HTTP error',path,e.code,e.read().decode()[:500]); raise
password_file=root/'test-password'
if not password_file.exists():
    password_file.write_text(secrets.token_urlsafe(24)); password_file.chmod(0o600)
if not (root/'wizard-initialized').exists():
    pw=password_file.read_text()
    request('Startup/User')
    request('Startup/Configuration',{'UICulture':'en-US','MetadataCountryCode':'US','PreferredMetadataLanguage':'en'})
    request('Startup/User',{'Name':'jellyp-test-admin','Password':pw})
    request('Startup/RemoteAccess',{'EnableRemoteAccess':False,'EnableAutomaticPortMapping':False})
    request('Startup/Complete',b'')
    (root/'wizard-initialized').touch()
pw=password_file.read_text();token=request('Users/AuthenticateByName',{'Username':'jellyp-test-admin','Pw':pw})['AccessToken']
plugins=request('Plugins'); assert any(p['Id'].replace('-','')=='0928e33489714205a455f42a76527f9e' for p in plugins),plugins
print('Plugin loaded; admin status:',request('JellyP/Status')['PendingRecovery'])
try:
    urllib.request.urlopen(base+'/JellyP/Status',timeout=10)
    raise AssertionError('Unauthenticated request allowed')
except urllib.error.HTTPError as e: assert e.code==401,e.code
page=request('web/ConfigurationPage?name=jellyp'); assert b'JellyPPolicies' in page
folders=request('Library/VirtualFolders')
if not any(f['Name']=='JellyP fixture' for f in folders):
    request('Library/VirtualFolders?name=JellyP%20fixture&collectionType=movies&refreshLibrary=true',{'LibraryOptions':{'PathInfos':[{'Path':str(root/'media')}],'EnableRealtimeMonitor':False,'SaveLocalMetadata':False,'EnableInternetProviders':False,'TypeOptions':[{'Type':'Movie','MetadataFetchers':[],'ImageFetchers':[]}]}})
for _ in range(120):
    items=request('Items?Recursive=true&IncludeItemTypes=Movie&Fields=Path')['Items']
    if items:break
    time.sleep(.5)
assert len(items)==1,items
item=items[0]; item_id=item['Id']; print('Fixture:',item['Name'],item_id)
# Valid PNG fixture, with enough entropy for meaningful WebP savings.
rng=random.Random(42)
rows=b''.join(b'\0'+b''.join(bytes((x//2,y//2,128+rng.randrange(5),255)) for x in range(512)) for y in range(512))
def chunk(kind,data):return struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data)&0xffffffff)
png=b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',512,512,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(rows))+chunk(b'IEND',b'')
request('Items/'+item_id+'/Images/Primary',base64.b64encode(png),ctype='image/png')
pluginid='0928e334-8971-4205-a455-f42a76527f9e'
config=request('Plugins/'+pluginid+'/Configuration');config.update(Enabled=True,DryRun=True,MaintenanceWindowConfirmed=True,IncludedItemIds=[item_id],BatchSize=1,MinimumSavingsBytes=0,MinimumSavingsRatio=0,MinimumSsim=.9,MinimumFreeSpaceBytes=67108864)
request('Plugins/'+pluginid+'/Configuration',config)
def run(path):
    before=request('JellyP/Status')['LastRun'].get('FinishedUtc')
    receipt=request('JellyP/'+path,b'')
    for _ in range(120):
        s=request('JellyP/Status')
        if s['LastRun'].get('ExecutionId')==receipt['ExecutionId'] and s['LastRun'].get('Phase') in ('Completed','Failed','Cancelled'):return s
        time.sleep(.25)
    raise AssertionError('Task timeout')
preview=run('Run');assert preview['LastRun']['EstimatedSavingsBytes']>0,preview
assert preview['LastRun']['Converted']==0
config['DryRun']=False;request('Plugins/'+pluginid+'/Configuration',config)
applied=run('Run');assert applied['LastRun']['Converted']==1,applied
record=applied['RecentRecords'][0];assert record['State']=='Committed',applied
assert request('Items/'+item_id+'/Images')[0]['ImageType']=='Primary'
# Persist output before we deliberately restart this isolated host later.
(root/'verified-item.json').write_text(json.dumps({'ItemId':item_id,'RecordId':record['Id'],'OutputPath':record['OutputPath'],'OutputHash':record['OutputHash']}))
print('Dry run and real migration passed:',record['OriginalBytes'],'->',record['OutputBytes'],'bytes')
config['Quality']=60;config['MinimumSsim']=0.8;request('Plugins/'+pluginid+'/Configuration',config)
synced=run('Sync');assert synced['LastRun']['Converted']==1,synced
latest=next(r for r in synced['RecentRecords'] if r['State']=='Committed')
request('JellyP/Rollback/'+latest['Id'],b'')
restored=request('JellyP/Status');assert restored['RetainedBackupBytes']==0,restored
print('Policy synchronization from original and exact rollback passed')
print('Artwork and byte reference validation complete; check restart persistence separately')
(root/'smoke-results.json').write_text(json.dumps({'Version':'12.1.0.0','ItemId':item_id,'PreviewSavedBytes':preview['LastRun']['EstimatedSavingsBytes'],'InitialSourceBytes':record['OriginalBytes'],'InitialWebpBytes':record['OutputBytes'],'Cases':['plugin load','admin auth','unauthenticated401','embedded UI','dry run','migration','policy synchronization','rollback']},indent=2))
