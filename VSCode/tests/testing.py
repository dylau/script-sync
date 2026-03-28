# add path for rh8_
import sys
sys.path.append(r'C:\Users\uk083720\Documents\dliu\04_Code\rh8-py39-utils')
import rh8_py39_utils as ru

print('hello world!!!')


import sys
import traceback
try:
    # original code here
    import rh8_py39_utils as ru
    ru.create_layer_if_not_exist(1)
except Exception as e:
    with open(__file__ + '.error', 'w') as f:
        f.write(traceback.format_exc())
    raise