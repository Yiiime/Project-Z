"""Fail closed if Unity did not actually execute and pass the PlayMode suite."""
import sys
import xml.etree.ElementTree as ET

root = ET.parse(sys.argv[1]).getroot()
cases = list(root.iter('test-case'))
failed = [case.get('fullname', case.get('name')) for case in cases
          if case.get('result') != 'Passed']
if not cases or failed or root.get('result') != 'Passed':
    raise SystemExit('Unity tests incomplete or failed: ' + repr(failed))
print('Verified Unity PlayMode results: %d passed' % len(cases))
