"""Build and verify a complete API secret update without logging secret values.
Pure functions only; GET secret metadata is never mistaken for existing values.
"""
import copy

def require(value,code):
    if not value:raise ValueError(code)

def _index(rows):
    require(isinstance(rows,list),'secret_inventory_invalid')
    result={}
    for item in rows:
        require(isinstance(item,dict) and isinstance(item.get('name'),str) and item['name'],'secret_entry_invalid')
        require(item['name'] not in result,'duplicate_secret_name')
        result[item['name']]=item
    return result

def existing_payload(metadata,retrieved):
    names=_index(metadata)
    require(isinstance(retrieved,dict) and isinstance(retrieved.get('value'),list),'secret_values_unavailable')
    values=_index(retrieved['value'])
    require(names.keys()==values.keys(),'secret_inventory_changed')
    output=[]
    for name,item in names.items():
        if item.get('keyVaultUrl'):
            require(isinstance(item['keyVaultUrl'],str) and item['keyVaultUrl'].startswith('https://'),'secret_reference_invalid')
            secret={'name':name,'keyVaultUrl':item['keyVaultUrl']}
            if 'identity' in item:
                require(isinstance(item['identity'],str) and item['identity'],'secret_identity_invalid')
                secret['identity']=item['identity']
        else:
            require(isinstance(values[name].get('value'),str),'existing_secret_value_missing')
            secret={'name':name,'value':values[name]['value']}
        output.append(secret)
    return output

def merged_payload(metadata,retrieved,additions):
    existing=existing_payload(metadata,retrieved)
    new=_index(additions)
    require(not set(new).intersection(x['name'] for x in existing),'secret_name_collision')
    for item in new.values():
        require(set(item)=={'name','value'} and isinstance(item['value'],str) and len(item['value'])>=32,'new_service_credential_invalid')
    return existing+copy.deepcopy(additions)

def unchanged_existing(before,metadata,retrieved):
    current=_index(existing_payload(metadata,retrieved))
    for item in before:
        require(current.get(item['name'])==item,'existing_secret_changed')
    return True
