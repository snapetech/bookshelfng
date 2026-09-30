import PropTypes from 'prop-types';
import React from 'react';
import Button from 'Components/Link/Button';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { scrollDirections } from 'Helpers/Props';
import InteractiveSearchConnector from 'InteractiveSearch/InteractiveSearchConnector';
import translate from 'Utilities/String/translate';

function BookInteractiveSearchModalContent(props) {
  const {
    bookId,
    seriesId,
    authorId,
    bookTitle,
    seriesTitle,
    authorName,
    onModalClose
  } = props;

  const hasSeries = seriesId !== null && seriesId !== undefined;
  const hasBook = bookId !== null;
  const searchPayload = hasSeries ? { seriesId, authorId } : { bookId };

  let modalHeader = translate('InteractiveSearchModalHeader');

  if (hasSeries) {
    modalHeader = translate('InteractiveSearchModalHeaderSeriesAuthor', { seriesTitle, authorName });
  } else if (hasBook) {
    modalHeader = translate('InteractiveSearchModalHeaderBookAuthor', { bookTitle, authorName });
  }

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {modalHeader}
      </ModalHeader>

      <ModalBody scrollDirection={scrollDirections.BOTH}>
        <InteractiveSearchConnector
          type="book"
          searchPayload={searchPayload}
        />
      </ModalBody>

      <ModalFooter>
        <Button onPress={onModalClose}>
          {translate('Close')}
        </Button>
      </ModalFooter>
    </ModalContent>
  );
}

BookInteractiveSearchModalContent.propTypes = {
  bookId: PropTypes.number,
  seriesId: PropTypes.number,
  authorId: PropTypes.number,
  bookTitle: PropTypes.string,
  seriesTitle: PropTypes.string,
  authorName: PropTypes.string.isRequired,
  onModalClose: PropTypes.func.isRequired
};

BookInteractiveSearchModalContent.defaultProps = {
  bookId: null
};

export default BookInteractiveSearchModalContent;
