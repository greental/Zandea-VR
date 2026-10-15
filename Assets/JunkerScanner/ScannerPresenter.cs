using UnityEngine;

namespace JunkerScanner
{
    public abstract class ScannerPresenter : MonoBehaviour
    {
        public abstract void Present(bool scanning, ScanInfo info);
    }
}
