using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ashburn.Interaction
{
    [RequireComponent(typeof(Collider2D))]
    public class SceneTransition : MonoBehaviour, IInteractable
    {
        [SerializeField] string prompt = "Move";
        [SerializeField] string sceneName;
        [SerializeField] string destinationSpawnName;
        [SerializeField] bool interactable = true;
        [SerializeField] bool locked;
        [SerializeField] Collider2D lockBarrier;

        public string Prompt => prompt;
        public bool IsLocked => locked;

        public bool CanInteract(GameObject interactor)
            => interactable && !locked && !string.IsNullOrWhiteSpace(sceneName);

        void Awake() => SyncLockBarrier();

        public void Lock()
        {
            locked = true;
            SyncLockBarrier();
        }

        public void Unlock()
        {
            locked = false;
            SyncLockBarrier();
        }

        public void SetLocked(bool value)
        {
            locked = value;
            SyncLockBarrier();
        }

        void SyncLockBarrier()
        {
            if (lockBarrier != null)
                lockBarrier.enabled = locked;
        }

        public void Interact(GameObject interactor)
        {
            if (!CanInteract(interactor))
                return;

            SceneSpawnRequest.DestinationSpawnName = destinationSpawnName;

            var travelerObject = interactor.transform.root.gameObject;
            var traveler = travelerObject.GetComponent<SceneTransitionTraveler>();
            if (traveler == null)
                traveler = travelerObject.AddComponent<SceneTransitionTraveler>();

            traveler.Travel(sceneName, destinationSpawnName);
        }
    }

    public class SceneTransitionTraveler : MonoBehaviour
    {
        bool _isTravelling;

        public void Travel(string sceneName, string destinationSpawnName)
        {
            if (_isTravelling)
                return;

            _isTravelling = true;
            StartCoroutine(LoadAndPlace(sceneName, destinationSpawnName));
        }

        IEnumerator LoadAndPlace(string sceneName, string destinationSpawnName)
        {
            DontDestroyOnLoad(gameObject);

            var operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (operation == null)
            {
                Debug.LogError($"Could not start loading scene '{sceneName}'.", this);
                _isTravelling = false;
                yield break;
            }

            yield return operation;

            var destinationScene = SceneManager.GetSceneByName(sceneName);
            if (destinationScene.IsValid() && destinationScene.isLoaded)
                SceneManager.MoveGameObjectToScene(gameObject, destinationScene);

            RemoveDuplicatePlayers();
            PlaceAt(destinationSpawnName);
            SceneSpawnRequest.DestinationSpawnName = null;
            Destroy(this);
        }

        void RemoveDuplicatePlayers()
        {
            foreach (var player in GameObject.FindGameObjectsWithTag("Player"))
            {
                if (player == gameObject || player.transform.IsChildOf(transform))
                    continue;

                player.SetActive(false);
                Destroy(player);
            }
        }

        void PlaceAt(string destinationSpawnName)
        {
            if (string.IsNullOrWhiteSpace(destinationSpawnName))
                return;

            var destination = GameObject.Find(destinationSpawnName);
            if (destination == null)
            {
                Debug.LogWarning(
                    $"Destination spawn '{destinationSpawnName}' was not found in scene " +
                    $"'{SceneManager.GetActiveScene().name}'.", this);
                return;
            }

            var position = destination.transform.position;
            transform.SetPositionAndRotation(position, destination.transform.rotation);

            var body = GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.position = position;
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
            }
        }
    }

    public static class SceneSpawnRequest
    {
        public static string DestinationSpawnName { get; set; }

        public static bool TryConsume(out string destinationSpawnName)
        {
            destinationSpawnName = DestinationSpawnName;
            DestinationSpawnName = null;
            return !string.IsNullOrWhiteSpace(destinationSpawnName);
        }
    }
}
