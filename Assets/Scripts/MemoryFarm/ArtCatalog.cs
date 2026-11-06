using UnityEngine;

namespace Deepspace
{
    // A list of pictures found by file name. Code loads this one asset from Resources, so the pictures themselves can live
    // in ART. A new picture goes into the Pictures list of its catalog.
    [CreateAssetMenu(menuName = "Memory Farm/Art Catalog")]
    public sealed class ArtCatalog : ScriptableObject
    {
        [SerializeField] private Texture2D[] pictures;

        public Texture2D Find(string pictureName)
        {
            foreach (var picture in pictures)
                if (picture != null && picture.name == pictureName) return picture;
            return null;
        }
    }
}
